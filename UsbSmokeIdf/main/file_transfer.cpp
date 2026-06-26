#include <ctype.h>
#include <dirent.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "esp_err.h"
#include "esp_log.h"
#include "esp_littlefs.h"

#include "file_transfer.h"

// [ACURATEX] Punto de montaje LittleFS visto por stdio: fopen("/fs/...").
#define APP_FS_BASE              "/fs"
// [ACURATEX] Etiqueta de particion definida en partitions.csv.
#define APP_FS_PARTITION_LABEL   "storage"
// [ACURATEX] Archivo interno que recuerda que programa esta seleccionado.
#define APP_SELECTED_META        APP_FS_BASE "/.selected"
// [ACURATEX] Archivo temporal usado mientras llega FILE_BEGIN/FILE_DATA/FILE_END.
#define APP_UPLOAD_TMP           APP_FS_BASE "/.upload.tmp"

// [ACURATEX] Nombre maximo de programa TXT aceptado por FILE_*.
#define APP_FILE_MAX_NAME_LEN    48
// [C/C++] constexpr crea una constante de compilacion con tipo. Aqui limita el
// tamano maximo de archivo transferido a 64 KiB.
static constexpr size_t APP_FILE_MAX_SIZE = 65536;
// [ACURATEX] FILE_GET_NEXT lee 32 bytes crudos por respuesta y los codifica en
// Base64 para transportarlos como texto.
#define APP_GET_CHUNK_SIZE       32

// [ESP-IDF] TAG identifica logs del modulo de transferencia de archivos.
static const char *TAG = "file_transfer";

// [ACURATEX] Estado de una subida FILE_BEGIN -> FILE_DATA* -> FILE_END.
typedef struct
{
    // [ACURATEX] Indica si hay una subida en curso.
    bool active;
    // [ACURATEX] Nombre final del archivo, sin ruta /fs.
    char name[APP_FILE_MAX_NAME_LEN + 1];
    // [ACURATEX] Tamano declarado por FILE_BEGIN.
    size_t expected_size;
    // [ACURATEX] Bytes ya decodificados y escritos en .upload.tmp.
    size_t received_size;
    // [ACURATEX] Indice FILE_DATA esperado. Evita chunks fuera de orden.
    uint32_t next_index;
    // [C/C++] FILE* es el handle stdio al archivo temporal abierto en LittleFS.
    FILE *fp;
} app_file_upload_t;

// [ACURATEX] Estado de una descarga FILE_GET -> FILE_GET_NEXT*.
typedef struct
{
    // [ACURATEX] Indica si hay una descarga en curso.
    bool active;
    // [ACURATEX] Nombre del archivo que se esta leyendo.
    char name[APP_FILE_MAX_NAME_LEN + 1];
    // [ACURATEX] Tamano total informado en FILE_BEGIN de descarga.
    size_t total_size;
    // [ACURATEX] Indice FILE_DATA que se enviara en la proxima respuesta.
    uint32_t next_index;
    // [C/C++] FILE* abierto en modo lectura sobre LittleFS.
    FILE *fp;
} app_file_download_t;

// [ACURATEX] true cuando LittleFS ya fue montado en /fs.
static bool s_fs_ready = false;
// [ACURATEX] Contexto global de subida. El protocolo admite una transferencia a
// la vez.
static app_file_upload_t s_upload;
// [ACURATEX] Contexto global de descarga. Tambien es una descarga a la vez.
static app_file_download_t s_download;
// [ACURATEX] Programa marcado como seleccionado. Se sincroniza con .selected.
static char s_selected_name[APP_FILE_MAX_NAME_LEN + 1] = {0};

/**
 * [POR QUE EXISTE]
 * Valida que un nombre FILE_* sea seguro para guardarse dentro de /fs.
 *
 * [QUIEN LA LLAMA]
 * Todas las rutas que abren, borran, seleccionan o consultan archivos.
 *
 * [CUANDO SE EJECUTA]
 * Antes de construir una ruta o acceder a LittleFS.
 *
 * [ENTRADAS]
 * Recibe nombre sin ruta.
 *
 * [SALIDAS]
 * Devuelve true si el nombre es aceptado por el protocolo.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * No usa globales; solo valida texto.
 *
 * [FLUJO ACURATEX]
 * FILE_BEGIN/GET/DELETE -> validar nombre -> construir /fs/nombre.
 *
 * [EQUIVALENCIA MCU]
 * Es validar una clave antes de escribir en EEPROM/flash.
 *
 * [SI NO EXISTIERA]
 * La app podria pedir rutas con `..`, separadores o nombres internos.
 */
static bool app_file_is_valid_name(const char *name)
{
    if (name == NULL || name[0] == '\0')
    {
        return false;
    }

    size_t len = strlen(name);
    if (len == 0 || len > APP_FILE_MAX_NAME_LEN)
    {
        return false;
    }

    // [ACURATEX] Estos nombres son reservados por el firmware y no se exponen
    // como archivos de programa.
    if (strcmp(name, ".selected") == 0 || strcmp(name, ".upload.tmp") == 0)
    {
        return false;
    }

    // [ACURATEX] Evita escape de directorio relativo.
    if (strstr(name, "..") != NULL)
    {
        return false;
    }

    for (size_t i = 0; i < len; i++)
    {
        char c = name[i];
        // [ACURATEX] `/` y `\` impedirian mantener todo dentro de /fs. `|` es
        // separador del protocolo FILE_*.
        if (c == '/' || c == '\\' || c == '|' || c == '\r' || c == '\n')
        {
            return false;
        }
    }

    return true;
}

/**
 * [POR QUE EXISTE]
 * Construye la ruta absoluta LittleFS para un archivo de programa.
 *
 * [QUIEN LA LLAMA]
 * Operaciones de existencia, tamano, lectura, escritura y borrado.
 *
 * [CUANDO SE EJECUTA]
 * Despues de validar el nombre.
 *
 * [ENTRADAS]
 * Buffer de salida, tamano y nombre de archivo.
 *
 * [SALIDAS]
 * No devuelve valor; escribe `/fs/nombre`.
 *
 * [ESTADO QUE MODIFICA]
 * Solo modifica el buffer de salida.
 *
 * [CONCURRENCIA]
 * No usa estado global.
 *
 * [FLUJO ACURATEX]
 * nombre TXT -> /fs/nombre -> fopen/remove/rename.
 *
 * [EQUIVALENCIA MCU]
 * Es mapear un nombre logico a direccion/ruta fisica.
 *
 * [SI NO EXISTIERA]
 * Cada funcion tendria que repetir el snprintf de ruta.
 */
static void app_build_path(char *out, size_t out_size, const char *name)
{
    snprintf(out, out_size, "%s/%s", APP_FS_BASE, name);
}

/**
 * [POR QUE EXISTE]
 * Convierte un caracter Base64 a su valor de 6 bits.
 *
 * [QUIEN LA LLAMA]
 * app_base64_decode().
 *
 * [CUANDO SE EJECUTA]
 * Por cada caracter Base64 de FILE_DATA recibido.
 *
 * [ENTRADAS]
 * Caracter ASCII.
 *
 * [SALIDAS]
 * 0..63 si es valido; -1 si no pertenece al alfabeto Base64.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Sin globales ni bloqueo.
 *
 * [FLUJO ACURATEX]
 * FILE_DATA texto -> valor 6 bits -> bytes binarios.
 *
 * [EQUIVALENCIA MCU]
 * Es una tabla de decodificacion compacta.
 *
 * [SI NO EXISTIERA]
 * El decodificador no podria reconstruir bytes desde texto.
 */
static int app_b64_value(char c)
{
    if (c >= 'A' && c <= 'Z') return c - 'A';
    if (c >= 'a' && c <= 'z') return c - 'a' + 26;
    if (c >= '0' && c <= '9') return c - '0' + 52;
    if (c == '+') return 62;
    if (c == '/') return 63;
    return -1;
}

/**
 * [POR QUE EXISTE]
 * Decodifica el payload Base64 de FILE_DATA a bytes para escribirlos en flash.
 *
 * [QUIEN LA LLAMA]
 * app_file_cmd_data().
 *
 * [CUANDO SE EJECUTA]
 * Por cada chunk FILE_DATA recibido durante una subida.
 *
 * [ENTRADAS]
 * Texto Base64, buffer de salida, capacidad y puntero de longitud producida.
 *
 * [SALIDAS]
 * Devuelve true si todos los caracteres fueron validos y el buffer alcanzo.
 *
 * [ESTADO QUE MODIFICA]
 * Escribe bytes en `out` y cantidad en `*out_len`.
 *
 * [CONCURRENCIA]
 * No usa estado global; trabaja con buffers locales.
 *
 * [FLUJO ACURATEX]
 * FILE_DATA|idx|base64 -> bytes -> fwrite(.upload.tmp).
 *
 * [EQUIVALENCIA MCU]
 * Es desempaquetar datos binarios transportados en ASCII.
 *
 * [SI NO EXISTIERA]
 * FILE_DATA solo podria transportar texto plano, no bytes arbitrarios.
 */
static bool app_base64_decode(const char *input, uint8_t *out, size_t out_max, size_t *out_len)
{
    uint32_t accum = 0;
    int bits = 0;
    size_t produced = 0;

    if (input == NULL || out == NULL || out_len == NULL)
    {
        return false;
    }

    *out_len = 0;

    for (size_t i = 0; input[i] != '\0'; i++)
    {
        char c = input[i];

        // [BASE64] '=' marca padding final; despues de esto no se producen mas
        // bytes.
        if (c == '=')
        {
            break;
        }

        if (isspace((unsigned char)c))
        {
            continue;
        }

        int v = app_b64_value(c);
        if (v < 0)
        {
            return false;
        }

        // [BASE64] Cada caracter aporta 6 bits al acumulador.
        accum = (accum << 6) | (uint32_t)v;
        bits += 6;

        while (bits >= 8)
        {
            bits -= 8;

            if (produced >= out_max)
            {
                return false;
            }

            // [BASE64] Cuando hay 8 bits disponibles, se extrae un byte.
            out[produced++] = (uint8_t)((accum >> bits) & 0xFF);
        }
    }

    *out_len = produced;
    return true;
}

/**
 * [POR QUE EXISTE]
 * Codifica bytes leidos desde LittleFS a Base64 para FILE_GET_NEXT.
 *
 * [QUIEN LA LLAMA]
 * app_file_cmd_get_next().
 *
 * [CUANDO SE EJECUTA]
 * Por cada chunk de descarga.
 *
 * [ENTRADAS]
 * Bytes crudos, longitud, buffer de salida y tamano.
 *
 * [SALIDAS]
 * true si genero texto Base64 completo.
 *
 * [ESTADO QUE MODIFICA]
 * Escribe el buffer `out`.
 *
 * [CONCURRENCIA]
 * Sin estado global.
 *
 * [FLUJO ACURATEX]
 * fread -> Base64 -> FILE_DATA|idx|texto.
 *
 * [EQUIVALENCIA MCU]
 * Es empaquetar bytes binarios en ASCII para un protocolo por lineas.
 *
 * [SI NO EXISTIERA]
 * FILE_GET no podria devolver bytes arbitrarios en una respuesta textual.
 */
static bool app_base64_encode(const uint8_t *input, size_t input_len, char *out, size_t out_size)
{
    static const char *tbl = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    size_t o = 0;

    if (out == NULL)
    {
        return false;
    }

    if (input_len == 0)
    {
        if (out_size < 1) return false;
        out[0] = '\0';
        return true;
    }

    for (size_t i = 0; i < input_len; i += 3)
    {
        uint32_t v = 0;
        int remain = (int)(input_len - i);

        // [BASE64] Tres bytes se agrupan en 24 bits y se dividen en cuatro
        // indices de 6 bits.
        v |= ((uint32_t)input[i]) << 16;
        if (remain > 1) v |= ((uint32_t)input[i + 1]) << 8;
        if (remain > 2) v |= ((uint32_t)input[i + 2]);

        if ((o + 4) >= out_size)
        {
            return false;
        }

        out[o++] = tbl[(v >> 18) & 0x3F];
        out[o++] = tbl[(v >> 12) & 0x3F];
        out[o++] = (remain > 1) ? tbl[(v >> 6) & 0x3F] : '=';
        out[o++] = (remain > 2) ? tbl[v & 0x3F] : '=';
    }

    out[o] = '\0';
    return true;
}

/**
 * [POR QUE EXISTE]
 * Cierra y limpia cualquier subida FILE_* en curso.
 *
 * [QUIEN LA LLAMA]
 * Inicio de nueva subida, errores de FILE_END e init.
 *
 * [CUANDO SE EJECUTA]
 * Antes de FILE_BEGIN nuevo o al terminar/cancelar una subida.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Cierra s_upload.fp si existe y pone s_upload en cero.
 *
 * [CONCURRENCIA]
 * No usa mutex; el protocolo procesa una linea FILE_* a la vez desde el handler.
 *
 * [FLUJO ACURATEX]
 * FILE_BEGIN nuevo/FILE_END -> limpiar estado de upload.
 *
 * [EQUIVALENCIA MCU]
 * Es cerrar un archivo temporal y reiniciar el contexto de recepcion.
 *
 * [SI NO EXISTIERA]
 * Una subida fallida podria dejar un FILE* abierto o indices viejos.
 */
static void app_upload_reset(void)
{
    if (s_upload.fp != NULL)
    {
        fclose(s_upload.fp);
        s_upload.fp = NULL;
    }

    // [C/C++] memset borra nombre, tamanos, indice y bandera active.
    memset(&s_upload, 0, sizeof(s_upload));
}

/**
 * [POR QUE EXISTE]
 * Cierra y limpia cualquier descarga FILE_GET en curso.
 *
 * [QUIEN LA LLAMA]
 * Inicio de FILE_GET, FILE_GET_NEXT al terminar, delete y init.
 *
 * [CUANDO SE EJECUTA]
 * Cuando una descarga termina, se reemplaza o se invalida.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Cierra s_download.fp y limpia s_download.
 *
 * [CONCURRENCIA]
 * Sin mutex propio; descarga es secuencial por protocolo.
 *
 * [FLUJO ACURATEX]
 * FILE_GET -> FILE_GET_NEXT* -> FILE_END -> reset.
 *
 * [EQUIVALENCIA MCU]
 * Es cerrar el archivo de lectura y reiniciar el cursor.
 *
 * [SI NO EXISTIERA]
 * FILE_GET_NEXT podria seguir usando un archivo viejo.
 */
static void app_download_reset(void)
{
    if (s_download.fp != NULL)
    {
        fclose(s_download.fp);
        s_download.fp = NULL;
    }

    memset(&s_download, 0, sizeof(s_download));
}

/**
 * [POR QUE EXISTE]
 * Comprueba si un archivo de programa existe en LittleFS.
 *
 * [QUIEN LA LLAMA]
 * SELECT, DELETE, INFO, GET y validacion de .selected.
 *
 * [CUANDO SE EJECUTA]
 * Antes de operaciones que requieren un archivo existente.
 *
 * [ENTRADAS]
 * Nombre logico del archivo.
 *
 * [SALIDAS]
 * true si pudo abrirlo en modo lectura.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado persistente.
 *
 * [CONCURRENCIA]
 * Abre y cierra un FILE* local.
 *
 * [FLUJO ACURATEX]
 * FILE_GET|nombre -> existe? -> abrir o ERR FILE_NOT_FOUND.
 *
 * [EQUIVALENCIA MCU]
 * Es consultar si un registro/archivo existe antes de usarlo.
 *
 * [SI NO EXISTIERA]
 * Las rutas tendrian que duplicar fopen solo para validar.
 */
static bool app_file_exists(const char *name)
{
    char path[96];
    FILE *fp;

    if (!app_file_is_valid_name(name))
    {
        return false;
    }

    app_build_path(path, sizeof(path), name);
    fp = fopen(path, "rb");
    if (fp == NULL)
    {
        return false;
    }

    fclose(fp);
    return true;
}

/**
 * [POR QUE EXISTE]
 * Obtiene el tamano real de un archivo LittleFS.
 *
 * [QUIEN LA LLAMA]
 * FILE_INFO y FILE_GET.
 *
 * [CUANDO SE EJECUTA]
 * Antes de responder informacion o iniciar descarga.
 *
 * [ENTRADAS]
 * Nombre y puntero de salida.
 *
 * [SALIDAS]
 * true si pudo calcular el tamano con fseek/ftell.
 *
 * [ESTADO QUE MODIFICA]
 * Escribe `*out_size`.
 *
 * [CONCURRENCIA]
 * Usa FILE* local.
 *
 * [FLUJO ACURATEX]
 * FILE_INFO/GET -> tamano -> respuesta SIZE.
 *
 * [EQUIVALENCIA MCU]
 * Es medir cuantos bytes hay en flash antes de transmitirlos.
 *
 * [SI NO EXISTIERA]
 * FILE_GET no podria anunciar el total de bytes.
 */
static bool app_file_get_size(const char *name, size_t *out_size)
{
    if (out_size == NULL || !app_file_is_valid_name(name))
    {
        return false;
    }

    char path[96];
    app_build_path(path, sizeof(path), name);

    FILE *fp = fopen(path, "rb");
    if (fp == NULL)
    {
        return false;
    }

    if (fseek(fp, 0, SEEK_END) != 0)
    {
        fclose(fp);
        return false;
    }

    long sz = ftell(fp);
    fclose(fp);

    if (sz < 0)
    {
        return false;
    }

    *out_size = (size_t)sz;
    return true;
}

/**
 * [POR QUE EXISTE]
 * Indica si un archivo es el programa seleccionado.
 *
 * [QUIEN LA LLAMA]
 * FILE_LIST y FILE_INFO.
 *
 * [CUANDO SE EJECUTA]
 * Al armar respuestas que deben marcar seleccion.
 *
 * [ENTRADAS]
 * Nombre de archivo.
 *
 * [SALIDAS]
 * true si coincide con s_selected_name.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * Lee una cadena global sin mutex.
 *
 * [FLUJO ACURATEX]
 * FILE_LIST -> marcar nombre con * si esta seleccionado.
 *
 * [EQUIVALENCIA MCU]
 * Es comparar contra la configuracion activa guardada.
 *
 * [SI NO EXISTIERA]
 * La app no sabria que programa esta activo.
 */
static bool app_file_is_selected(const char *name)
{
    return (s_selected_name[0] != '\0' && strcmp(s_selected_name, name) == 0);
}

/**
 * [POR QUE EXISTE]
 * Carga desde LittleFS el nombre del programa seleccionado.
 *
 * [QUIEN LA LLAMA]
 * app_fs_mount().
 *
 * [CUANDO SE EJECUTA]
 * Al montar LittleFS durante init o primer uso.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor.
 *
 * [ESTADO QUE MODIFICA]
 * Actualiza s_selected_name y puede borrar .selected si es invalido.
 *
 * [CONCURRENCIA]
 * Usa FILE* local y variable global de seleccion.
 *
 * [FLUJO ACURATEX]
 * mount -> leer /fs/.selected -> seleccion disponible para FILE_LIST/INFO.
 *
 * [EQUIVALENCIA MCU]
 * Es cargar una preferencia persistida en flash.
 *
 * [SI NO EXISTIERA]
 * La seleccion se perderia al reiniciar.
 */
static void app_selected_load(void)
{
    FILE *fp = fopen(APP_SELECTED_META, "rb");
    if (fp == NULL)
    {
        s_selected_name[0] = '\0';
        return;
    }

    size_t n = fread(s_selected_name, 1, sizeof(s_selected_name) - 1, fp);
    fclose(fp);

    s_selected_name[n] = '\0';

    // [ACURATEX] El metadato debe ser solo el nombre; CR/LF se recortan.
    char *p = strpbrk(s_selected_name, "\r\n");
    if (p != NULL)
    {
        *p = '\0';
    }

    if (!app_file_is_valid_name(s_selected_name) || !app_file_exists(s_selected_name))
    {
        s_selected_name[0] = '\0';
        remove(APP_SELECTED_META);
    }
}

/**
 * [POR QUE EXISTE]
 * Persiste o borra el programa seleccionado.
 *
 * [QUIEN LA LLAMA]
 * FILE_SELECT, FILE_DELETE y app_selected_load cuando invalida metadato.
 *
 * [CUANDO SE EJECUTA]
 * Al cambiar seleccion o limpiar una seleccion borrada.
 *
 * [ENTRADAS]
 * Nombre a guardar o NULL/vacio para limpiar.
 *
 * [SALIDAS]
 * ESP_OK o ESP_FAIL si LittleFS no pudo escribir.
 *
 * [ESTADO QUE MODIFICA]
 * Actualiza s_selected_name y /fs/.selected.
 *
 * [CONCURRENCIA]
 * Usa FILE* local.
 *
 * [FLUJO ACURATEX]
 * FILE_SELECT|prog -> .selected -> reinicio conserva seleccion.
 *
 * [EQUIVALENCIA MCU]
 * Es guardar una configuracion activa en memoria no volatil.
 *
 * [SI NO EXISTIERA]
 * FILE_SELECT solo viviria en RAM.
 */
static esp_err_t app_selected_save(const char *name)
{
    if (name == NULL || name[0] == '\0')
    {
        s_selected_name[0] = '\0';
        remove(APP_SELECTED_META);
        return ESP_OK;
    }

    FILE *fp = fopen(APP_SELECTED_META, "wb");
    if (fp == NULL)
    {
        return ESP_FAIL;
    }

    size_t len = strlen(name);
    if (fwrite(name, 1, len, fp) != len)
    {
        fclose(fp);
        return ESP_FAIL;
    }

    fclose(fp);

    strncpy(s_selected_name, name, sizeof(s_selected_name) - 1);
    s_selected_name[sizeof(s_selected_name) - 1] = '\0';
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Monta LittleFS en /fs para que FILE_* pueda usar fopen/readdir/remove.
 *
 * [QUIEN LA LLAMA]
 * app_file_transfer_init() y app_file_transfer_process_line() si aun no esta
 * listo.
 *
 * [CUANDO SE EJECUTA]
 * Al arrancar y como recuperacion perezosa antes de procesar comandos FILE_*.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * ESP_OK si LittleFS queda montado; error de esp_vfs_littlefs_register si falla.
 *
 * [ESTADO QUE MODIFICA]
 * s_fs_ready y s_selected_name.
 *
 * [CONCURRENCIA]
 * No usa mutex; se espera uso secuencial desde inicializacion/comandos.
 *
 * [FLUJO ACURATEX]
 * app_main -> file_transfer_init -> mount /fs -> FILE_* disponible.
 *
 * [EQUIVALENCIA MCU]
 * Es inicializar el sistema de archivos sobre flash.
 *
 * [SI NO EXISTIERA]
 * FILE_BEGIN/GET/LIST/DELETE no tendrian almacenamiento.
 */
static esp_err_t app_fs_mount(void)
{
    if (s_fs_ready)
    {
        return ESP_OK;
    }

    // [ESP-IDF] Esta estructura registra LittleFS como VFS en /fs.
    esp_vfs_littlefs_conf_t conf{};
    conf.base_path = APP_FS_BASE;
    conf.partition_label = APP_FS_PARTITION_LABEL;
    conf.partition = NULL;
    conf.blockdev = NULL;
    conf.read_only = false;
    // [LITTLEFS] Si el montaje falla, ESP-IDF formatea la particion. Se conserva
    // exactamente la configuracion existente.
    conf.format_if_mount_failed = true;
    conf.dont_mount = false;
    conf.grow_on_mount = false;

    // [ESP-IDF] esp_vfs_littlefs_register monta y expone LittleFS a funciones C
    // como fopen(), fread(), fwrite(), opendir().
    esp_err_t ret = esp_vfs_littlefs_register(&conf);
    if (ret != ESP_OK)
    {
        ESP_LOGE(TAG,
                 "FS|MOUNT|FAIL|BASE=%s|PART=%s|ERR=%s",
                 APP_FS_BASE,
                 APP_FS_PARTITION_LABEL,
                 esp_err_to_name(ret));
        return ret;
    }

    size_t total = 0;
    size_t used = 0;
    ret = esp_littlefs_info(APP_FS_PARTITION_LABEL, &total, &used);
    if (ret == ESP_OK)
    {
        ESP_LOGI(TAG,
                 "FS|MOUNT|OK|BASE=%s|PART=%s|TOTAL=%u|USED=%u",
                 APP_FS_BASE,
                 APP_FS_PARTITION_LABEL,
                 (unsigned)total,
                 (unsigned)used);
    }
    else
    {
        ESP_LOGW(TAG,
                 "FS|MOUNT|OK|BASE=%s|PART=%s|INFO=%s",
                 APP_FS_BASE,
                 APP_FS_PARTITION_LABEL,
                 esp_err_to_name(ret));
    }

    s_fs_ready = true;
    ESP_LOGI(TAG, "FILE_FS_READY|BASE=%s|PART=%s", APP_FS_BASE, APP_FS_PARTITION_LABEL);
    app_selected_load();
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Inicia una subida de archivo desde la app hacia LittleFS.
 *
 * [QUIEN LA LLAMA]
 * app_file_transfer_process_line() para `FILE_BEGIN|nombre|tamano`.
 *
 * [CUANDO SE EJECUTA]
 * Antes de recibir chunks FILE_DATA.
 *
 * [ENTRADAS]
 * Nombre, tamano decimal como texto, buffer de respuesta.
 *
 * [SALIDAS]
 * Responde ACK FILE_BEGIN o ERR FILE_BEGIN/FILE_SIZE/FILE_FS.
 *
 * [ESTADO QUE MODIFICA]
 * Reinicia s_upload, abre /fs/.upload.tmp y guarda nombre/tamano/indice.
 *
 * [CONCURRENCIA]
 * Una subida a la vez; no hay mutex propio.
 *
 * [FLUJO ACURATEX]
 * FILE_BEGIN -> .upload.tmp abierto -> espera FILE_DATA indice 0.
 *
 * [EQUIVALENCIA MCU]
 * Es preparar un buffer de escritura en flash antes de recibir bloques.
 *
 * [SI NO EXISTIERA]
 * FILE_DATA no tendria archivo temporal ni tamano esperado.
 */
static esp_err_t app_file_cmd_begin(const char *name, const char *size_str, char *response, size_t response_size)
{
    char *endptr = NULL;
    unsigned long parsed_size;
    FILE *fp;

    if (!app_file_is_valid_name(name) || size_str == NULL)
    {
        snprintf(response, response_size, "ERR FILE_BEGIN");
        return ESP_OK;
    }

    // [ACURATEX] El tamano declarado debe ser decimal y no superar 64 KiB.
    parsed_size = strtoul(size_str, &endptr, 10);
    if (endptr == size_str || *endptr != '\0' || parsed_size > APP_FILE_MAX_SIZE)
    {
        snprintf(response, response_size, "ERR FILE_SIZE");
        return ESP_OK;
    }

    app_upload_reset();
    // [ACURATEX] Se elimina cualquier temporal anterior antes de empezar.
    remove(APP_UPLOAD_TMP);

    fp = fopen(APP_UPLOAD_TMP, "wb");
    if (fp == NULL)
    {
        snprintf(response, response_size, "ERR FILE_FS");
        return ESP_OK;
    }

    s_upload.active = true;
    s_upload.fp = fp;
    strncpy(s_upload.name, name, sizeof(s_upload.name) - 1);
    s_upload.name[sizeof(s_upload.name) - 1] = '\0';
    s_upload.expected_size = (size_t)parsed_size;
    s_upload.received_size = 0;
    // [ACURATEX] El primer chunk valido debe ser FILE_DATA|0|...
    s_upload.next_index = 0;

    snprintf(response, response_size, "ACK FILE_BEGIN %s", name);
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Recibe un chunk Base64 de una subida y lo escribe en el temporal.
 *
 * [QUIEN LA LLAMA]
 * app_file_transfer_process_line() para `FILE_DATA|indice|base64`.
 *
 * [CUANDO SE EJECUTA]
 * Entre FILE_BEGIN y FILE_END.
 *
 * [ENTRADAS]
 * Indice decimal, payload Base64 y buffer de respuesta.
 *
 * [SALIDAS]
 * ACK FILE_DATA indice o ERR FILE_STATE/DATA/INDEX/B64/SIZE/FS.
 *
 * [ESTADO QUE MODIFICA]
 * Escribe bytes en .upload.tmp, suma received_size e incrementa next_index.
 *
 * [CONCURRENCIA]
 * Usa el FILE* de s_upload; el protocolo exige chunks ordenados.
 *
 * [FLUJO ACURATEX]
 * FILE_DATA|0|... -> decode -> fwrite -> ACK -> espera indice 1.
 *
 * [EQUIVALENCIA MCU]
 * Es recibir bloques numerados y escribirlos secuencialmente en flash.
 *
 * [SI NO EXISTIERA]
 * FILE_BEGIN abriria una transferencia pero nunca recibiria contenido.
 */
static esp_err_t app_file_cmd_data(const char *index_str, const char *b64, char *response, size_t response_size)
{
    char *endptr = NULL;
    unsigned long parsed_index;
    // [ACURATEX] Buffer temporal del chunk decodificado. El Base64 recibido debe
    // caber aqui despues de decodificar.
    uint8_t decoded[128];
    size_t decoded_len = 0;

    if (!s_upload.active || s_upload.fp == NULL)
    {
        snprintf(response, response_size, "ERR FILE_STATE");
        return ESP_OK;
    }

    if (index_str == NULL || b64 == NULL)
    {
        snprintf(response, response_size, "ERR FILE_DATA");
        return ESP_OK;
    }

    parsed_index = strtoul(index_str, &endptr, 10);
    if (endptr == index_str || *endptr != '\0')
    {
        snprintf(response, response_size, "ERR FILE_INDEX");
        return ESP_OK;
    }

    // [ACURATEX] Indices estrictamente secuenciales evitan reordenamiento o
    // repeticion accidental de chunks.
    if ((uint32_t)parsed_index != s_upload.next_index)
    {
        snprintf(response, response_size, "ERR FILE_INDEX");
        return ESP_OK;
    }

    if (!app_base64_decode(b64, decoded, sizeof(decoded), &decoded_len))
    {
        snprintf(response, response_size, "ERR FILE_B64");
        return ESP_OK;
    }

    // [ACURATEX] Nunca se escribe mas que el tamano declarado ni mas que el
    // limite global de 64 KiB.
    if ((s_upload.received_size + decoded_len) > s_upload.expected_size ||
        (s_upload.received_size + decoded_len) > APP_FILE_MAX_SIZE)
    {
        snprintf(response, response_size, "ERR FILE_SIZE");
        return ESP_OK;
    }

    if (decoded_len > 0)
    {
        size_t wrote = fwrite(decoded, 1, decoded_len, s_upload.fp);
        if (wrote != decoded_len)
        {
            snprintf(response, response_size, "ERR FILE_FS");
            return ESP_OK;
        }
    }

    s_upload.received_size += decoded_len;
    s_upload.next_index++;

    snprintf(response, response_size, "ACK FILE_DATA %lu", parsed_index);
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Cierra una subida y convierte el temporal en archivo final.
 *
 * [QUIEN LA LLAMA]
 * app_file_transfer_process_line() para `FILE_END|nombre`.
 *
 * [CUANDO SE EJECUTA]
 * Despues de recibir todos los FILE_DATA esperados.
 *
 * [ENTRADAS]
 * Nombre esperado y buffer de respuesta.
 *
 * [SALIDAS]
 * ACK FILE_END nombre SIZE=n o ERR FILE_END/FILE_SIZE/FILE_FS.
 *
 * [ESTADO QUE MODIFICA]
 * Cierra s_upload.fp, renombra .upload.tmp al nombre final y limpia s_upload.
 *
 * [CONCURRENCIA]
 * Opera sobre la unica subida activa.
 *
 * [FLUJO ACURATEX]
 * FILE_END -> verificar tamano -> rename temporal -> archivo listo.
 *
 * [EQUIVALENCIA MCU]
 * Es confirmar una escritura completa antes de hacerla visible como archivo.
 *
 * [SI NO EXISTIERA]
 * Los archivos quedarian como temporales o incompletos.
 */
static esp_err_t app_file_cmd_end(const char *name, char *response, size_t response_size)
{
    char final_path[96];

    if (!s_upload.active || s_upload.fp == NULL || name == NULL || strcmp(name, s_upload.name) != 0)
    {
        snprintf(response, response_size, "ERR FILE_END");
        return ESP_OK;
    }

    if (s_upload.received_size != s_upload.expected_size)
    {
        snprintf(response, response_size, "ERR FILE_SIZE");
        return ESP_OK;
    }

    fclose(s_upload.fp);
    s_upload.fp = NULL;

    app_build_path(final_path, sizeof(final_path), s_upload.name);
    // [ACURATEX] Se reemplaza el archivo final si ya existia.
    remove(final_path);

    if (rename(APP_UPLOAD_TMP, final_path) != 0)
    {
        app_upload_reset();
        snprintf(response, response_size, "ERR FILE_FS");
        return ESP_OK;
    }

    snprintf(response, response_size, "ACK FILE_END %s SIZE=%u",
             s_upload.name,
             (unsigned)s_upload.received_size);

    app_upload_reset();
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Lista los archivos de programa visibles en LittleFS.
 *
 * [QUIEN LA LLAMA]
 * app_file_transfer_process_line() para `FILE_LIST`.
 *
 * [CUANDO SE EJECUTA]
 * Bajo pedido de la app.
 *
 * [ENTRADAS]
 * Buffer de respuesta.
 *
 * [SALIDAS]
 * FILE_LIST, FILE_LIST EMPTY o ERR FILE_FS.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica archivos.
 *
 * [CONCURRENCIA]
 * Usa DIR* local de opendir/readdir/closedir.
 *
 * [FLUJO ACURATEX]
 * FILE_LIST -> readdir(/fs) -> nombres separados por coma.
 *
 * [EQUIVALENCIA MCU]
 * Es enumerar registros guardados en flash.
 *
 * [SI NO EXISTIERA]
 * La app no podria mostrar programas disponibles.
 */
static esp_err_t app_file_cmd_list(char *response, size_t response_size)
{
    ESP_LOGI(TAG, "FILE_LIST_BEGIN");
    DIR *dir = opendir(APP_FS_BASE);
    if (dir == NULL)
    {
        snprintf(response, response_size, "ERR FILE_FS");
        return ESP_OK;
    }

    response[0] = '\0';
    snprintf(response, response_size, "FILE_LIST");

    bool any = false;
    unsigned count = 0;
    struct dirent *entry;

    while ((entry = readdir(dir)) != NULL)
    {
        const char *name = entry->d_name;

        if (strcmp(name, ".") == 0 || strcmp(name, "..") == 0)
        {
            continue;
        }

        // [ACURATEX] Se ocultan archivos internos de control.
        if (strcmp(name, ".selected") == 0 || strcmp(name, ".upload.tmp") == 0)
        {
            continue;
        }

        // [ACURATEX] El asterisco marca el programa seleccionado.
        const char *mark = app_file_is_selected(name) ? "*" : "";
        size_t cur = strlen(response);

        int wrote = snprintf(response + cur,
                             (cur < response_size) ? (response_size - cur) : 0,
                             any ? ",%s%s" : " %s%s",
                             name,
                             mark);
        if (wrote < 0)
        {
            break;
        }

        any = true;
        count++;
    }

    closedir(dir);

    if (!any)
    {
        snprintf(response, response_size, "FILE_LIST EMPTY");
    }

    ESP_LOGI(TAG, "FILE_LIST_RESULT|COUNT=%u", count);
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Marca un archivo existente como seleccionado.
 *
 * [QUIEN LA LLAMA]
 * app_file_transfer_process_line() para `FILE_SELECT|nombre`.
 *
 * [CUANDO SE EJECUTA]
 * Cuando la app elige el programa activo.
 *
 * [ENTRADAS]
 * Nombre y buffer de respuesta.
 *
 * [SALIDAS]
 * ACK FILE_SELECT, ERR FILE_NOT_FOUND o ERR FILE_FS.
 *
 * [ESTADO QUE MODIFICA]
 * s_selected_name y /fs/.selected.
 *
 * [CONCURRENCIA]
 * No usa mutex; seleccion es una cadena global.
 *
 * [FLUJO ACURATEX]
 * FILE_SELECT -> guardar .selected -> FILE_LIST marca *.
 *
 * [EQUIVALENCIA MCU]
 * Es seleccionar un perfil/receta persistente.
 *
 * [SI NO EXISTIERA]
 * La app podria subir archivos pero no marcar uno como activo.
 */
static esp_err_t app_file_cmd_select(const char *name, char *response, size_t response_size)
{
    if (!app_file_exists(name))
    {
        snprintf(response, response_size, "ERR FILE_NOT_FOUND");
        return ESP_OK;
    }

    if (app_selected_save(name) != ESP_OK)
    {
        snprintf(response, response_size, "ERR FILE_FS");
        return ESP_OK;
    }

    snprintf(response, response_size, "ACK FILE_SELECT %s", name);
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Borra un archivo de programa de LittleFS.
 *
 * [QUIEN LA LLAMA]
 * app_file_transfer_process_line() para `FILE_DELETE|nombre`.
 *
 * [CUANDO SE EJECUTA]
 * Bajo pedido de la app.
 *
 * [ENTRADAS]
 * Nombre y buffer de respuesta.
 *
 * [SALIDAS]
 * ACK FILE_DELETE, ERR FILE_NOT_FOUND o ERR FILE_FS.
 *
 * [ESTADO QUE MODIFICA]
 * Elimina el archivo, puede limpiar seleccion y resetear descarga activa.
 *
 * [CONCURRENCIA]
 * Opera sobre estado global de seleccion/descarga.
 *
 * [FLUJO ACURATEX]
 * FILE_DELETE -> remove(/fs/nombre) -> ACK.
 *
 * [EQUIVALENCIA MCU]
 * Es borrar una receta de la flash.
 *
 * [SI NO EXISTIERA]
 * La memoria LittleFS solo creceria con archivos obsoletos.
 */
static esp_err_t app_file_cmd_delete(const char *name, char *response, size_t response_size)
{
    char path[96];

    if (!app_file_exists(name))
    {
        snprintf(response, response_size, "ERR FILE_NOT_FOUND");
        return ESP_OK;
    }

    app_build_path(path, sizeof(path), name);
    if (remove(path) != 0)
    {
        snprintf(response, response_size, "ERR FILE_FS");
        return ESP_OK;
    }

    // [ACURATEX] Si se borra el archivo seleccionado, se borra tambien la
    // metadata .selected.
    if (strcmp(s_selected_name, name) == 0)
    {
        app_selected_save(NULL);
    }

    if (s_download.active && strcmp(s_download.name, name) == 0)
    {
        app_download_reset();
    }

    snprintf(response, response_size, "ACK FILE_DELETE %s", name);
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Devuelve tamano y seleccion de un archivo.
 *
 * [QUIEN LA LLAMA]
 * app_file_transfer_process_line() para `FILE_INFO|nombre`.
 *
 * [CUANDO SE EJECUTA]
 * Bajo consulta de la app.
 *
 * [ENTRADAS]
 * Nombre y buffer de respuesta.
 *
 * [SALIDAS]
 * FILE_INFO|nombre|SIZE=n|SELECTED=0/1 o ERR FILE_NOT_FOUND.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica archivos.
 *
 * [CONCURRENCIA]
 * Lee tamano y seleccion global.
 *
 * [FLUJO ACURATEX]
 * FILE_INFO -> stat por fopen/fseek/ftell -> respuesta.
 *
 * [EQUIVALENCIA MCU]
 * Es consultar metadatos de una receta en flash.
 *
 * [SI NO EXISTIERA]
 * La app tendria que descargar o listar para inferir informacion.
 */
static esp_err_t app_file_cmd_info(const char *name, char *response, size_t response_size)
{
    size_t size = 0;

    if (!app_file_exists(name) || !app_file_get_size(name, &size))
    {
        snprintf(response, response_size, "ERR FILE_NOT_FOUND");
        return ESP_OK;
    }

    snprintf(response,
             response_size,
             "FILE_INFO|%s|SIZE=%u|SELECTED=%u",
             name,
             (unsigned)size,
             app_file_is_selected(name) ? 1U : 0U);

    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Inicia la descarga de un archivo desde LittleFS hacia la app.
 *
 * [QUIEN LA LLAMA]
 * app_file_transfer_process_line() para `FILE_GET|nombre`.
 *
 * [CUANDO SE EJECUTA]
 * Antes de la serie FILE_GET_NEXT.
 *
 * [ENTRADAS]
 * Nombre y buffer de respuesta.
 *
 * [SALIDAS]
 * FILE_BEGIN|nombre|tamano o ERR FILE_NOT_FOUND/FILE_FS.
 *
 * [ESTADO QUE MODIFICA]
 * Abre s_download.fp, guarda nombre, tamano e indice 0.
 *
 * [CONCURRENCIA]
 * Una descarga a la vez; reinicia descarga anterior.
 *
 * [FLUJO ACURATEX]
 * FILE_GET -> FILE_BEGIN de descarga -> app pide FILE_GET_NEXT.
 *
 * [EQUIVALENCIA MCU]
 * Es abrir una lectura secuencial desde flash.
 *
 * [SI NO EXISTIERA]
 * La app no podria recuperar archivos guardados.
 */
static esp_err_t app_file_cmd_get(const char *name, char *response, size_t response_size)
{
    char path[96];
    size_t size = 0;
    FILE *fp;

    if (!app_file_exists(name) || !app_file_get_size(name, &size))
    {
        snprintf(response, response_size, "ERR FILE_NOT_FOUND");
        return ESP_OK;
    }

    app_download_reset();

    app_build_path(path, sizeof(path), name);
    fp = fopen(path, "rb");
    if (fp == NULL)
    {
        snprintf(response, response_size, "ERR FILE_FS");
        return ESP_OK;
    }

    s_download.active = true;
    s_download.fp = fp;
    s_download.total_size = size;
    s_download.next_index = 0;
    strncpy(s_download.name, name, sizeof(s_download.name) - 1);
    s_download.name[sizeof(s_download.name) - 1] = '\0';

    snprintf(response, response_size, "FILE_BEGIN|%s|%u", s_download.name, (unsigned)s_download.total_size);
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Entrega el siguiente chunk Base64 de una descarga activa.
 *
 * [QUIEN LA LLAMA]
 * app_file_transfer_process_line() para `FILE_GET_NEXT`.
 *
 * [CUANDO SE EJECUTA]
 * Repetidamente despues de FILE_GET hasta llegar a EOF.
 *
 * [ENTRADAS]
 * Buffer de respuesta.
 *
 * [SALIDAS]
 * FILE_DATA|indice|base64, FILE_END|nombre o ERR FILE_GET_STATE/FILE_B64/FILE_FS.
 *
 * [ESTADO QUE MODIFICA]
 * Avanza s_download.next_index y cierra/reset al llegar a EOF.
 *
 * [CONCURRENCIA]
 * Usa el FILE* global de descarga.
 *
 * [FLUJO ACURATEX]
 * FILE_GET_NEXT -> fread 32 bytes -> Base64 -> FILE_DATA|idx.
 *
 * [EQUIVALENCIA MCU]
 * Es transmitir flash en paginas pequenas codificadas como texto.
 *
 * [SI NO EXISTIERA]
 * FILE_GET solo anunciaria el archivo pero no podria enviar su contenido.
 */
static esp_err_t app_file_cmd_get_next(char *response, size_t response_size)
{
    if (!s_download.active || s_download.fp == NULL)
    {
        snprintf(response, response_size, "ERR FILE_GET_STATE");
        return ESP_OK;
    }

    // [ACURATEX] Chunk crudo de 32 bytes antes de convertir a Base64.
    uint8_t raw[APP_GET_CHUNK_SIZE];
    size_t n = fread(raw, 1, sizeof(raw), s_download.fp);

    if (n > 0)
    {
        char b64[64];
        if (!app_base64_encode(raw, n, b64, sizeof(b64)))
        {
            snprintf(response, response_size, "ERR FILE_B64");
            return ESP_OK;
        }

        snprintf(response,
                 response_size,
                 "FILE_DATA|%u|%s",
                 (unsigned)s_download.next_index,
                 b64);

        s_download.next_index++;
        return ESP_OK;
    }

    if (ferror(s_download.fp))
    {
        app_download_reset();
        snprintf(response, response_size, "ERR FILE_FS");
        return ESP_OK;
    }

    snprintf(response, response_size, "FILE_END|%s", s_download.name);
    app_download_reset();
    return ESP_OK;
}

/**
 * [POR QUE EXISTE]
 * Inicializa el modulo FILE_* y monta LittleFS.
 *
 * [QUIEN LA LLAMA]
 * app_main() durante el arranque.
 *
 * [CUANDO SE EJECUTA]
 * Una vez al iniciar el firmware.
 *
 * [ENTRADAS]
 * No recibe parametros.
 *
 * [SALIDAS]
 * No devuelve valor; registra error si el montaje falla.
 *
 * [ESTADO QUE MODIFICA]
 * Limpia upload/download, seleccion en RAM y s_fs_ready si monta bien.
 *
 * [CONCURRENCIA]
 * Corre antes del uso normal de FILE_*.
 *
 * [FLUJO ACURATEX]
 * app_main -> file_transfer_init -> LittleFS listo para FILE_*.
 *
 * [EQUIVALENCIA MCU]
 * Es preparar el sistema de archivos antes de comandos externos.
 *
 * [SI NO EXISTIERA]
 * El primer comando FILE_* tendria que inicializar todo sin estado limpio previo.
 */
void app_file_transfer_init(void)
{
    app_upload_reset();
    app_download_reset();
    s_selected_name[0] = '\0';

    if (app_fs_mount() != ESP_OK)
    {
        ESP_LOGE(TAG, "FS|MOUNT|FAIL|INIT");
    }
}

/**
 * [POR QUE EXISTE]
 * Detecta si una linea pertenece al protocolo FILE_*.
 *
 * [QUIEN LA LLAMA]
 * command_processor.cpp antes de clasificar CAN o texto generico.
 *
 * [CUANDO SE EJECUTA]
 * Por cada linea recibida desde USB/TCP/UART.
 *
 * [ENTRADAS]
 * Linea ya normalizada.
 *
 * [SALIDAS]
 * true si empieza o coincide con un comando FILE soportado.
 *
 * [ESTADO QUE MODIFICA]
 * No modifica estado.
 *
 * [CONCURRENCIA]
 * No bloquea.
 *
 * [FLUJO ACURATEX]
 * Transporte -> command_processor -> FILE_*? -> process_line.
 *
 * [EQUIVALENCIA MCU]
 * Es un clasificador de comandos por prefijo.
 *
 * [SI NO EXISTIERA]
 * FILE_* caeria como texto generico o CAN.
 */
bool app_file_transfer_is_command(const char *line)
{
    if (line == NULL)
    {
        return false;
    }

    return (strncmp(line, "FILE_BEGIN|", 11) == 0) ||
           (strncmp(line, "FILE_DATA|", 10) == 0) ||
           (strncmp(line, "FILE_END|", 9) == 0) ||
           (strcmp(line, "FILE_LIST") == 0) ||
           (strncmp(line, "FILE_SELECT|", 12) == 0) ||
           (strncmp(line, "FILE_DELETE|", 12) == 0) ||
           (strncmp(line, "FILE_INFO|", 10) == 0) ||
           (strncmp(line, "FILE_GET|", 9) == 0) ||
           (strcmp(line, "FILE_GET_NEXT") == 0);
}

/**
 * [POR QUE EXISTE]
 * Ejecuta un comando FILE_* y genera una unica respuesta textual.
 *
 * [QUIEN LA LLAMA]
 * command_processor.cpp despues de app_file_transfer_is_command().
 *
 * [CUANDO SE EJECUTA]
 * Cada vez que la app envia FILE_BEGIN, FILE_DATA, FILE_END, FILE_LIST,
 * FILE_SELECT, FILE_DELETE, FILE_INFO, FILE_GET o FILE_GET_NEXT.
 *
 * [ENTRADAS]
 * Linea de comando, buffer de respuesta y tamano del buffer.
 *
 * [SALIDAS]
 * ESP_OK si pudo procesar el protocolo y escribe ACK/FILE_/ERR en response;
 * ESP_ERR_INVALID_ARG si los punteros son invalidos.
 *
 * [ESTADO QUE MODIFICA]
 * Segun el comando, puede abrir/cerrar archivos, escribir LittleFS, borrar,
 * cambiar seleccion, avanzar upload/download o montar el FS.
 *
 * [CONCURRENCIA]
 * No crea tareas ni mutex. Se apoya en el flujo de lineas del transporte y en
 * estado global secuencial de upload/download.
 *
 * [FLUJO ACURATEX]
 * FILE_* -> strtok_r por `|` -> handler concreto -> response -> app.
 *
 * [EQUIVALENCIA MCU]
 * Es un interprete de comandos de almacenamiento en flash.
 *
 * [SI NO EXISTIERA]
 * La app no podria subir, listar, descargar ni borrar programas TXT.
 */
esp_err_t app_file_transfer_process_line(const char *line, char *response, size_t response_size)
{
    char buffer[192];
    char *saveptr = NULL;
    char *cmd;
    char *arg1;
    char *arg2;

    if (line == NULL || response == NULL || response_size == 0)
    {
        return ESP_ERR_INVALID_ARG;
    }

    response[0] = '\0';

    if (!s_fs_ready)
    {
        if (app_fs_mount() != ESP_OK)
        {
            snprintf(response, response_size, "ERR FILE_FS");
            return ESP_OK;
        }
    }

    // [C/C++] strtok_r modifica el buffer, por eso se copia la linea recibida.
    strncpy(buffer, line, sizeof(buffer) - 1);
    buffer[sizeof(buffer) - 1] = '\0';

    // [ACURATEX] El protocolo FILE_* usa `|` como separador fijo.
    cmd = strtok_r(buffer, "|", &saveptr);
    arg1 = strtok_r(NULL, "|", &saveptr);
    arg2 = strtok_r(NULL, "|", &saveptr);

    if (cmd == NULL)
    {
        snprintf(response, response_size, "ERR FILE_CMD");
        return ESP_OK;
    }

    if (strcmp(cmd, "FILE_BEGIN") == 0)
    {
        return app_file_cmd_begin(arg1, arg2, response, response_size);
    }

    if (strcmp(cmd, "FILE_DATA") == 0)
    {
        return app_file_cmd_data(arg1, arg2, response, response_size);
    }

    if (strcmp(cmd, "FILE_END") == 0)
    {
        return app_file_cmd_end(arg1, response, response_size);
    }

    if (strcmp(cmd, "FILE_LIST") == 0)
    {
        return app_file_cmd_list(response, response_size);
    }

    if (strcmp(cmd, "FILE_SELECT") == 0)
    {
        return app_file_cmd_select(arg1, response, response_size);
    }

    if (strcmp(cmd, "FILE_DELETE") == 0)
    {
        return app_file_cmd_delete(arg1, response, response_size);
    }

    if (strcmp(cmd, "FILE_INFO") == 0)
    {
        return app_file_cmd_info(arg1, response, response_size);
    }

    if (strcmp(cmd, "FILE_GET") == 0)
    {
        return app_file_cmd_get(arg1, response, response_size);
    }

    if (strcmp(cmd, "FILE_GET_NEXT") == 0)
    {
        return app_file_cmd_get_next(response, response_size);
    }

    snprintf(response, response_size, "ERR FILE_CMD");
    return ESP_OK;
}
