# Detalle de implementacion del compilador ACX

## Contexto

Esta rama agrega el compilador ACX v1 para exportar perfiles completos de SQLite a archivos `.acx` binarios, con lector C# para validacion, pruebas de round-trip y rechazo temprano de archivos que exceden el limite provisional de 1 MiB.

## Archivos modificados

- `app_windows/AcuratexControlApp/Models/Profiles/ProfileModels.cs`
- `app_windows/AcuratexControlApp/Data/Sqlite/Importers/InitialProfileSeeds.cs`
- `app_windows/AcuratexControlApp/Repositories/Profiles/SqliteProfileRepository.Read.cs`
- `app_windows/AcuratexControlApp/Repositories/Profiles/SqliteProfileRepository.Write.cs`
- `app_windows/AcuratexControlApp/Repositories/Profiles/SqliteProfileRepository.Public.cs`
- `app_windows/AcuratexControlApp/Repositories/Profiles/SqliteProfileRepository.Actions.cs`
- `app_windows/AcuratexControlApp/Data/Sqlite/ProfileDatabaseOptions.cs`
- `app_windows/AcuratexControlApp/Services/Profiles/ProfileServiceCollectionExtensions.cs`
- `app_windows/AcuratexControlApp/Services/Profiles/ProfileValidator.cs`
- `app_windows/AcuratexControlApp.Tests/ProfileSqliteInfrastructureTests.cs`
- `app_windows/AcuratexControlApp.Tests/ProfileAcxExportTests.cs`
- `app_windows/AcuratexControlApp/Services/Profiles/Acx/AcxBinaryWriter.cs`
- `app_windows/AcuratexControlApp/Services/Profiles/Acx/AcxCrc32.cs`
- `app_windows/AcuratexControlApp/Services/Profiles/Acx/AcxFormatConstants.cs`
- `app_windows/AcuratexControlApp/Services/Profiles/Acx/AcxModels.cs`
- `app_windows/AcuratexControlApp/Services/Profiles/Acx/AcxProfileCompiler.cs`
- `app_windows/AcuratexControlApp/Services/Profiles/Acx/AcxProfileExportService.cs`
- `app_windows/AcuratexControlApp/Services/Profiles/Acx/AcxProfilePackageReader.cs`
- `docs/acx/ESPECIFICACION_ACX_V1.md`
- `DETALLE_IMPLEMENTACION_COMPILADOR_ACX.md`

## Tamano y CRC de P1 y P2

Valores obtenidos por el exportador real ACX v1 y verificados sobre los bytes finales exportados. Cada perfil se exporto dos veces y ambos binarios coincidieron byte por byte.

| Perfil | Archivo | Tamano | CRC32 payload |
| --- | --- | --- | --- |
| Programa 1 | `programa-1_v1.acx` | 5471 bytes | `0x5CAE00B7` |
| Programa 2 | `programa-2_v1.acx` | 5477 bytes | `0x2CFB7499` |

- No encontre evidencia local reproducible de `0x5CAF00B7` en los artefactos del compilador actual.
- La exportacion real y las pruebas repetibles dan `0x5CAE00B7` para Programa 1, asi que trato `0x5CAF00B7` como una referencia desactualizada o transcrita incorrectamente.
- El limite de tamano ACX v1 de `1 MiB = 1,048,576 bytes` es provisional y se revisara cuando exista el lector ACX del ESP32 y se midan particiones, almacenamiento y RAM reales.

## Ubicacion real de exportacion

El codigo exporta en:

- `%LOCALAPPDATA%\AcuratexFastControl\AcuratexControlApp\Exports`

En esta ejecucion se resolvio a:

- `C:\Users\Manuel22\AppData\Local\AcuratexFastControl\AcuratexControlApp\Exports`

Los paquetes ACX no se guardan dentro del repositorio, `bin` ni `obj`.

## Pruebas automatizadas

Se agregaron 12 pruebas ACX nuevas en `app_windows/AcuratexControlApp.Tests/ProfileAcxExportTests.cs` (13 casos de ejecucion contando la teoria por dos perfiles).

Cobertura principal:

- exportar Programa 1 a ACX
- exportar Programa 2 a ACX
- leer ambos paquetes con el lector C#
- comparar el paquete leido contra el modelo SQLite original
- verificar bytes CAN, DLC, INIT, TESTEO, posiciones, secuencias, Yarn, Stitch y FEET
- verificar CRC correcto
- verificar CRC y tamano reales de Programa 1 y Programa 2
- verificar exportacion determinista
- verificar accion dinamica no vacia
- rechazar paquetes ACX mayores a 1 MiB en compilador y lector
- detectar byte alterado
- detectar archivo truncado
- detectar magic incorrecto
- detectar version ACX no soportada
- verificar que una exportacion fallida no registre `exported_packages`
- verificar limpieza del temporal
- verificar offsets y longitudes dentro del archivo

## Resultados de restore/build/test

- `dotnet restore app_windows/AcuratexControlApp/AcuratexControlApp.sln`
  - Resultado: exito, todos los proyectos estaban actualizados para restauracion.
- `dotnet build app_windows/AcuratexControlApp/AcuratexControlApp.sln`
  - Resultado: exito.
  - Advertencias existentes no relacionadas con ACX en `CabezalDashboardTarjetas.razor`: 5.
- `dotnet test app_windows/AcuratexControlApp/AcuratexControlApp.sln`
  - Resultado: el comando termino con exito, pero en este entorno solo imprimio la restauracion; la verificacion real de pruebas se hizo con el proyecto de tests directo.
- `dotnet test app_windows/AcuratexControlApp.Tests/AcuratexControlApp.Tests.csproj --no-restore -v minimal`
  - Resultado: exito, 21 pruebas totales superadas.
- `dotnet test app_windows/AcuratexControlApp.Tests/AcuratexControlApp.Tests.csproj --no-restore -v minimal --filter "FullyQualifiedName~ProfileAcxExportTests"`
  - Resultado: exito, 13 casos ACX superados.

## Limitaciones conocidas

- El firmware ESP32 no fue modificado.
- El lector ACX en C/C++ para ESP32 no existe todavia.
- El sistema modular Programa 1 / Programa 2 / Programa 3 no fue modificado.
- Las pantallas y dashboards no fueron modificados.
- `AcxProfilePackageReader` es solo para validacion y pruebas en C#.
- La carga futura en ESP32 debe implementar el parser ACX y mapear secciones a `profile_store`.

## Auditoria CAN1/CAN2

Hallazgos documentales del codigo actual:

- La seleccion del bus fisico hoy se decide en `UsbSmokeIdf/main/can_driver_twai.cpp` y `UsbSmokeIdf/main/command_processor.cpp`.
- `app_can_select_bus(int bus)` solo actualiza una seleccion logica (`CAN1`/`CAN2`) y `app_can_send_standard(int bus, ...)` transmite sobre la unica pila TWAI disponible segun ese bus logico.
- `command_processor.cpp` expone los comandos `can1` y `can2`; si no hay bus seleccionado, el envio directo cae en `CAN1`.
- `head_state_manager.cpp` normaliza el bus con una regla logica simple: `2 -> CAN2`, cualquier otro valor -> `CAN1`.
- Las entidades que ya contienen un campo relacionado con bus son `HeadInitStepModel.Bus`, `ProfileActionStepModel.Bus`, `init_steps.bus`, `stop_profiles.bus` y `action_steps.bus`; en STOP ese dato no se conserva de extremo a extremo.
- Las entidades que no contienen bus son `HeadTesteoCommandProfileModel`, `HeadMotionCommandProfileModel`, `HeadJCommandProfileModel` y `HeadCascadeCommandProfileModel`, ademas de `testeo_profiles`, `motion_modules`, `motion_sequences`, `j_modules` y `cascade_modules` en SQLite.
- En STOP, `stop_profiles` si contiene `bus`, `InsertStop` escribe `bus = 1`, `LoadStop` lee esa columna pero no la incorpora al modelo recuperado, `HeadStopCommandProfileModel` no tiene un campo `Bus`, y la seccion STOP de ACX v1 no transporta bus; por eso STOP no conserva actualmente el bus de extremo a extremo.
- Falta una regla de producto explicita para decidir si la ruta fisica debe quedar fijada en firmware, derivarse por modulo o vivir como una regla logica del perfil. Esta observacion queda pendiente para la futura decision arquitectonica CAN1/CAN2.
- Tambien falta la confirmacion del hardware objetivo para saber si `CAN1` y `CAN2` son dos controladores separados, dos buses logicos o una abstraccion de configuracion.
- En esta tarea no se agregaron buses nuevos al modelo, SQLite ni ACX, y no se asumio ninguna ruta fisica.

## Siguiente paso para firmware

Implementar en `UsbSmokeIdf` un lector ACX v1 que:

1. Verifique magic, version, tamano y CRC32.
2. Lea el directorio de secciones.
3. Reconstruya el perfil runtime en memoria.
4. Valide offsets y longitudes antes de aceptar el paquete.
5. Cargue el resultado en `profile_store` de forma atomica.

## Confirmaciones

- El firmware no fue modificado.
- El sistema modular no fue modificado.
- SQLite no se consulta durante la serializacion de acciones CAN; el compilador serializa un `ProfileVersionDocument` ya cargado.
- Los paquetes ACX son deterministas.
- P1 y P2 se pueden recuperar sin perdida desde ACX.
- No se hizo commit ni push.
