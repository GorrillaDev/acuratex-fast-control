#pragma once

#include <stdint.h>

// [ACURATEX] Limite unico para lineas de comando copiadas entre tareas.
// Sale de los buffers actuales de USB/TCP/UART: 192 bytes incluyendo terminador.
#define APP_COMMAND_MAX_LINE_LENGTH 192

// [ACURATEX] Limite de respuesta copiada en la cola de respuestas.
// Debe contener el HEAD_STATUS completo actual y deja margen para campos
// futuros. Incluye el terminador NUL.
#define APP_REPLY_MAX_LINE_LENGTH 384
// [ACURATEX] Buffer de staging para agregar '\n' al enviar por USB/TCP/UART
// sin truncar la linea ya validada por la cola.
#define APP_REPLY_TX_BUFFER_SIZE (APP_REPLY_MAX_LINE_LENGTH + 1)

// [ACURATEX] Tamanos de cola elegidos para picos cortos sin reservar memoria
// enorme: 12 comandos (~2.4 KB) y 16 respuestas (~6.0 KB).
#define APP_COMMAND_INGRESS_QUEUE_LENGTH 12
#define APP_REPLY_QUEUE_LENGTH 16
// [ACURATEX] La cola fisica es mas corta: contiene comandos que pueden tocar
// Cabezal/CAN y deben drenarse en Core 1 sin acumular trabajo ilimitado.
#define APP_HEAD_COMMAND_QUEUE_LENGTH 8

typedef enum
{
    APP_TRANSPORT_USB = 0,
    APP_TRANSPORT_TCP,
    APP_TRANSPORT_UART,
} app_transport_type_t;

typedef struct
{
    app_transport_type_t transport;
    uint32_t session_id;
} app_reply_route_t;
