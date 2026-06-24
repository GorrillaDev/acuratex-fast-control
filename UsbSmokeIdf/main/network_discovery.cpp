#include <errno.h>
#include <stdio.h>
#include <string.h>

#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

#include "esp_log.h"
#include "lwip/inet.h"
#include "lwip/netdb.h"
#include "lwip/sockets.h"

#include "network_discovery.h"

// [ACURATEX] Este archivo implementa el "discovery" UDP de la ruta WiFi.
// Su trabajo no es procesar comandos HEAD_* ni FILE_*; solo permite que la app
// encuentre en la red local que IP/puerto TCP debe usar para hablar con el
// firmware cuando USB no esta activo.

// [ACURATEX] Texto exacto que debe enviar la app por UDP para descubrir equipos.
#define APP_DISCOVERY_QUERY "ACURATEX_DISCOVER"

// [ESP-IDF] TAG identifica en los logs que el mensaje viene de este modulo.
static const char *TAG = "network_discovery";

// [C/C++] volatile advierte que esta bandera puede cambiar fuera del flujo local
// de la tarea UDP, por ejemplo desde app_network_discovery_stop().
static volatile bool s_running = false;
// [FREERTOS] TaskHandle_t guarda el identificador de la tarea app_discovery.
static TaskHandle_t s_task_handle = NULL;
// [LWIP] Descriptor del socket UDP. -1 significa "no hay socket abierto".
static int s_socket_fd = -1;
// [ACURATEX] Copia local de la informacion que se responde por UDP.
static app_discovery_info_t s_info = {};
// [C/C++] Buffers static: viven durante toda la ejecucion del firmware.
// Se usan porque s_info guarda punteros a texto que debe seguir existiendo
// mientras la tarea UDP esta activa.
static char s_hostname[32] = "";
static char s_ip[16] = "";
static char s_ssid[33] = "";

/**
 * [POR QUE EXISTE]
 * Esta funcion centraliza el cierre del socket UDP de discovery.
 *
 * [QUIEN LA LLAMA]
 * La llaman app_network_discovery_task() al terminar y
 * app_network_discovery_stop() para desbloquear recvfrom().
 *
 * [CUANDO SE EJECUTA]
 * Cuando se apaga discovery, cuando falla bind() o cuando USB toma prioridad y
 * app_main detiene los servicios de red.
 *
 * [ENTRADAS]
 * No recibe parametros; usa el descriptor global s_socket_fd.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Cierra s_socket_fd si estaba abierto y lo deja en -1.
 *
 * [CONCURRENCIA]
 * Puede ejecutarse desde app_main mientras la tarea UDP esta bloqueada en
 * recvfrom(). shutdown()/close() fuerzan que la tarea despierte y pueda salir.
 *
 * [FLUJO ACURATEX]
 * USB activo o parada de red -> cerrar UDP -> discovery deja de responder.
 *
 * [EQUIVALENCIA MCU]
 * Es como deshabilitar un periferico de recepcion para que una tarea bloqueada
 * salga de espera.
 *
 * [SI NO EXISTIERA]
 * El cierre del socket quedaria repetido y la tarea podria quedarse bloqueada.
 */
static void app_discovery_close_socket(void)
{
    if (s_socket_fd >= 0)
    {
        // [LWIP] shutdown despierta operaciones bloqueadas sobre el socket.
        shutdown(s_socket_fd, SHUT_RDWR);
        // [LWIP] close libera el descriptor asignado por socket().
        close(s_socket_fd);
        s_socket_fd = -1;
    }
}

/**
 * [POR QUE EXISTE]
 * Esta funcion decide si un datagrama UDP recibido es una consulta Acuratex.
 *
 * [QUIEN LA LLAMA]
 * La llama app_network_discovery_task() por cada paquete recibido.
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que recvfrom() entrega bytes desde la red.
 *
 * [ENTRADAS]
 * Recibe puntero al buffer recibido y longitud real del datagrama.
 *
 * [SALIDAS]
 * Devuelve true si el paquete empieza con APP_DISCOVERY_QUERY.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Corre dentro de la tarea UDP y no bloquea.
 *
 * [FLUJO ACURATEX]
 * App envia ACURATEX_DISCOVER -> validar texto -> responder datos de conexion.
 *
 * [EQUIVALENCIA MCU]
 * Es un comparador de cabecera de protocolo antes de procesar un paquete.
 *
 * [SI NO EXISTIERA]
 * El firmware responderia a cualquier UDP o duplicaria la validacion en la tarea.
 */
static bool app_discovery_matches(const char *rx, int len)
{
    // [C/C++] size_t representa tamanos sin signo, como longitud de strings.
    size_t query_len = strlen(APP_DISCOVERY_QUERY);

    if (rx == NULL || len < (int)query_len)
    {
        return false;
    }

    // [ACURATEX] Se compara solo el prefijo para conservar el protocolo actual.
    return strncmp(rx, APP_DISCOVERY_QUERY, query_len) == 0;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion arma y envia la respuesta UDP de discovery a la app.
 *
 * [QUIEN LA LLAMA]
 * La llama app_network_discovery_task() despues de reconocer una consulta valida.
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que llega ACURATEX_DISCOVER al puerto APP_DISCOVERY_UDP_PORT.
 *
 * [ENTRADAS]
 * Recibe la direccion UDP del cliente que envio la consulta.
 *
 * [SALIDAS]
 * No devuelve valor; informa errores por ESP_LOGW.
 *
 * [ESTADO QUE MODIFICA]
 * No cambia estado logico; solo escribe por s_socket_fd.
 *
 * [CONCURRENCIA]
 * Corre en la tarea discovery. sendto() usa el socket UDP compartido con esa
 * misma tarea; app_network_discovery_stop() podria cerrarlo para apagar red.
 *
 * [FLUJO ACURATEX]
 * Discovery request -> respuesta con hostname, IP, puerto TCP, SSID y puerto UDP.
 *
 * [EQUIVALENCIA MCU]
 * Es como contestar por un bus broadcast diciendo "yo estoy en esta direccion".
 *
 * [SI NO EXISTIERA]
 * La app tendria que conocer manualmente IP y puerto TCP del firmware.
 */
static void app_discovery_send_reply(const struct sockaddr_in *client_addr)
{
    char tx[192];
    int len;

    if (client_addr == NULL || s_socket_fd < 0)
    {
        return;
    }

    // [ACURATEX] Formato textual de respuesta usado por la aplicacion para
    // construir la conexion TCP posterior.
    len = snprintf(tx,
                   sizeof(tx),
                   "ACURATEX_DEVICE|name=Acuratex Control Bridge|hostname=%s|ip=%s|tcp_port=%d|ssid=%s|discovery_port=%d\n",
                   s_hostname,
                   s_ip,
                   s_info.tcp_port,
                   s_ssid,
                   APP_DISCOVERY_UDP_PORT);
    if (len <= 0)
    {
        return;
    }
    if (len >= (int)sizeof(tx))
    {
        // [ACURATEX] Si la respuesta excede el buffer, se recorta conservando
        // salto final. No se cambia el formato ni el tamano en este estudio.
        len = sizeof(tx) - 1;
        tx[len - 1] = '\n';
    }

    // [LWIP] sendto envia un datagrama UDP a una direccion concreta; no requiere
    // una conexion previa como TCP.
    int sent = sendto(s_socket_fd,
                      tx,
                      len,
                      0,
                      (const struct sockaddr *)client_addr,
                      sizeof(*client_addr));
    if (sent < 0)
    {
        ESP_LOGW(TAG, "sendto discovery fallo errno=%d", errno);
    }
    else
    {
        ESP_LOGI(TAG,
                 "Discovery respondido a %s tcp=%d",
                 inet_ntoa(client_addr->sin_addr),
                 s_info.tcp_port);
    }
}

/**
 * [POR QUE EXISTE]
 * Esta tarea FreeRTOS mantiene abierto el puerto UDP de discovery.
 *
 * [QUIEN LA LLAMA]
 * La crea app_network_discovery_start() con xTaskCreate().
 *
 * [CUANDO SE EJECUTA]
 * Mientras la ruta de red esta activa: WiFi conectado y servidor TCP corriendo.
 *
 * [ENTRADAS]
 * Recibe arg de FreeRTOS, pero este firmware no lo usa.
 *
 * [SALIDAS]
 * No devuelve; al terminar llama vTaskDelete(NULL).
 *
 * [ESTADO QUE MODIFICA]
 * Actualiza s_socket_fd, s_running y s_task_handle.
 *
 * [CONCURRENCIA]
 * Es una tarea separada de app_main y de app_tcp_server. Puede quedar bloqueada
 * en recvfrom(); app_network_discovery_stop() cierra el socket para desbloquearla.
 *
 * [FLUJO ACURATEX]
 * TCP activo -> tarea UDP escucha -> app descubre IP/puerto -> app abre TCP.
 *
 * [EQUIVALENCIA MCU]
 * Es un "loop()" dedicado a un puerto UDP, ejecutado como tarea FreeRTOS.
 *
 * [SI NO EXISTIERA]
 * La red podria funcionar por IP fija/manual, pero no habria descubrimiento
 * automatico para la aplicacion.
 */
static void app_network_discovery_task(void *arg)
{
    (void)arg;

    // [LWIP] socket(AF_INET, SOCK_DGRAM, IPPROTO_IP) crea un socket IPv4 UDP.
    int fd = socket(AF_INET, SOCK_DGRAM, IPPROTO_IP);
    if (fd < 0)
    {
        ESP_LOGE(TAG, "socket UDP discovery fallo errno=%d", errno);
        s_running = false;
        s_task_handle = NULL;
        vTaskDelete(NULL);
        return;
    }

    s_socket_fd = fd;

    int opt = 1;
    // [LWIP] SO_REUSEADDR permite reutilizar el puerto si hubo un cierre previo.
    setsockopt(fd, SOL_SOCKET, SO_REUSEADDR, &opt, sizeof(opt));
    // [LWIP] SO_BROADCAST permite manejar trafico broadcast de discovery.
    setsockopt(fd, SOL_SOCKET, SO_BROADCAST, &opt, sizeof(opt));

    struct sockaddr_in bind_addr = {};
    bind_addr.sin_family = AF_INET;
    // [LWIP] INADDR_ANY escucha en cualquier IP local asignada al ESP32.
    bind_addr.sin_addr.s_addr = htonl(INADDR_ANY);
    // [LWIP] htons convierte el puerto a orden de bytes de red.
    bind_addr.sin_port = htons(APP_DISCOVERY_UDP_PORT);

    // [LWIP] bind asocia el socket UDP al puerto fijo de discovery.
    if (bind(fd, (struct sockaddr *)&bind_addr, sizeof(bind_addr)) != 0)
    {
        ESP_LOGE(TAG, "bind UDP discovery puerto=%d fallo errno=%d", APP_DISCOVERY_UDP_PORT, errno);
        app_discovery_close_socket();
        s_running = false;
        s_task_handle = NULL;
        vTaskDelete(NULL);
        return;
    }

    ESP_LOGI(TAG,
             "Discovery UDP escuchando puerto=%d hostname=%s tcp=%d",
             APP_DISCOVERY_UDP_PORT,
             s_hostname,
             s_info.tcp_port);

    while (s_running)
    {
        char rx[128];
        struct sockaddr_in client_addr = {};
        socklen_t addr_len = sizeof(client_addr);
        // [LWIP] recvfrom bloquea hasta recibir un datagrama UDP o hasta que el
        // socket se cierre desde app_network_discovery_stop().
        int len = recvfrom(fd,
                           rx,
                           sizeof(rx) - 1,
                           0,
                           (struct sockaddr *)&client_addr,
                           &addr_len);
        if (len < 0)
        {
            if (s_running)
            {
                ESP_LOGW(TAG, "recvfrom discovery fallo errno=%d", errno);
            }
            break;
        }

        // [C/C++] Se agrega terminador nulo para poder tratar rx como string.
        rx[len] = '\0';
        if (app_discovery_matches(rx, len))
        {
            ESP_LOGI(TAG, "Discovery request desde %s", inet_ntoa(client_addr.sin_addr));
            app_discovery_send_reply(&client_addr);
        }
    }

    app_discovery_close_socket();
    ESP_LOGI(TAG, "Discovery UDP detenido");
    s_running = false;
    s_task_handle = NULL;
    vTaskDelete(NULL);
}

/**
 * [POR QUE EXISTE]
 * Esta funcion inicia el servicio UDP de discovery con la informacion actual de
 * WiFi/TCP.
 *
 * [QUIEN LA LLAMA]
 * La llama app_poll_network_without_usb() cuando WiFi esta conectado y el
 * servidor TCP ya esta corriendo.
 *
 * [CUANDO SE EJECUTA]
 * Al habilitar la ruta de red o cuando cambia la informacion que debe anunciarse.
 *
 * [ENTRADAS]
 * Recibe app_discovery_info_t con hostname, IP, SSID y puerto TCP.
 *
 * [SALIDAS]
 * Devuelve ESP_OK si discovery ya estaba correcto o si crea la tarea; devuelve
 * ESP_ERR_INVALID_ARG o ESP_ERR_NO_MEM si no puede iniciar.
 *
 * [ESTADO QUE MODIFICA]
 * Copia strings a s_hostname/s_ip/s_ssid, actualiza s_info, s_running y
 * s_task_handle.
 *
 * [CONCURRENCIA]
 * Crea una tarea FreeRTOS con stack 4096 y prioridad 4. Si ya habia una tarea
 * con datos distintos, la detiene antes de crear otra.
 *
 * [FLUJO ACURATEX]
 * WiFi IP + TCP puerto -> start discovery -> app encuentra el firmware.
 *
 * [EQUIVALENCIA MCU]
 * Es habilitar un servicio auxiliar de identificacion en red.
 *
 * [SI NO EXISTIERA]
 * app_main no podria arrancar discovery aunque TCP ya estuviera listo.
 */
esp_err_t app_network_discovery_start(const app_discovery_info_t *info)
{
    if (info == NULL || info->tcp_port <= 0 || info->tcp_port > 65535)
    {
        return ESP_ERR_INVALID_ARG;
    }

    if (s_task_handle != NULL)
    {
        if (s_running &&
            s_info.tcp_port == info->tcp_port &&
            strcmp(s_hostname, info->hostname != NULL ? info->hostname : "") == 0 &&
            strcmp(s_ip, info->ip != NULL ? info->ip : "") == 0)
        {
            return ESP_OK;
        }

        // [ACURATEX] Si los datos cambiaron, se reinicia discovery para que la
        // respuesta UDP anuncie informacion vigente.
        app_network_discovery_stop();
    }

    // [C/C++] Se copian los textos porque info apunta a buffers de otro modulo.
    strlcpy(s_hostname, info->hostname != NULL ? info->hostname : "", sizeof(s_hostname));
    strlcpy(s_ip, info->ip != NULL ? info->ip : "", sizeof(s_ip));
    strlcpy(s_ssid, info->ssid != NULL ? info->ssid : "", sizeof(s_ssid));

    // [C/C++] s_info guarda punteros a las copias static anteriores.
    s_info.hostname = s_hostname;
    s_info.ip = s_ip;
    s_info.ssid = s_ssid;
    s_info.tcp_port = info->tcp_port;
    s_running = true;

    // [FREERTOS] xTaskCreate lanza la tarea que escuchara UDP; no queda ejecutada
    // dentro de app_main.
    BaseType_t ok = xTaskCreatePinnedToCore(app_network_discovery_task,
                                            "app_discovery",
                                            4096,
                                            NULL,
                                            4,
                                            &s_task_handle,
                                            0);
    if (ok != pdPASS)
    {
        s_running = false;
        s_task_handle = NULL;
        ESP_LOGE(TAG, "No se pudo crear tarea discovery UDP");
        return ESP_ERR_NO_MEM;
    }

    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Esta funcion detiene el servicio UDP de discovery.
 *
 * [QUIEN LA LLAMA]
 * La llaman app_stop_network_for_usb(), app_poll_network_without_usb() y
 * app_network_discovery_start() si debe reiniciar la tarea.
 *
 * [CUANDO SE EJECUTA]
 * Al montar USB, perder WiFi/TCP o cambiar la informacion anunciada.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Pone s_running en false, cierra socket y espera que s_task_handle quede NULL.
 *
 * [CONCURRENCIA]
 * Puede bloquear hasta 100 ciclos de 10 ms mientras la tarea UDP sale. La espera
 * usa vTaskDelay() para ceder CPU a FreeRTOS.
 *
 * [FLUJO ACURATEX]
 * USB activo o TCP detenido -> stop discovery -> app deja de descubrir por UDP.
 *
 * [EQUIVALENCIA MCU]
 * Es una parada ordenada de un servicio de comunicaciones.
 *
 * [SI NO EXISTIERA]
 * Discovery podria seguir anunciando un TCP que ya no esta disponible.
 */
void app_network_discovery_stop(void)
{
    if (s_task_handle == NULL && !s_running)
    {
        return;
    }

    ESP_LOGI(TAG, "Deteniendo discovery UDP");
    s_running = false;
    app_discovery_close_socket();

    for (int i = 0; i < 100 && s_task_handle != NULL; i++)
    {
        // [FREERTOS] La espera no ocupa CPU continuamente; cede el scheduler.
        vTaskDelay(pdMS_TO_TICKS(10));
    }
}

/**
 * [POR QUE EXISTE]
 * Esta funcion informa si discovery UDP esta activo.
 *
 * [QUIEN LA LLAMA]
 * La llama app_main para decidir si debe iniciar o detener discovery.
 *
 * [CUANDO SE EJECUTA]
 * En el bucle principal de firmware, junto con las consultas de WiFi y TCP.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * Devuelve true si la bandera esta activa y existe handle de tarea.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lee variables escritas por app_network_discovery_task() y stop().
 *
 * [FLUJO ACURATEX]
 * app_main -> consultar discovery -> evitar duplicar tarea UDP.
 *
 * [EQUIVALENCIA MCU]
 * Es una bandera de "servicio habilitado".
 *
 * [SI NO EXISTIERA]
 * app_main no sabria si discovery ya esta corriendo.
 */
bool app_network_discovery_is_running(void)
{
    return s_running && s_task_handle != NULL;
}
