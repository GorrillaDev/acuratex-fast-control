// [ACURATEX] Catalogo central de permisos usados por el sistema demo.
// [FLUJO] Rol -> permiso -> pantalla habilitada o bloqueada.
namespace AcuratexControlApp.Models.Auth;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para agrupar en un solo punto todos los permisos simbolicos del sistema.
///
/// [QUIEN LA USA]
/// La usan servicios de autorizacion, roles y pantallas.
///
/// [CUANDO SE USA]
/// Se usa cada vez que la app compara permisos.
///
/// [ENTRADAS]
/// No recibe entradas.
///
/// [SALIDAS]
/// Expone constantes y la coleccion completa de permisos.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Rol -> permisos -> UI habilitada o no.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un mapa de bits de autorizaciones.
///
/// [SI NO EXISTIERA]
/// Cada pantalla inventaria sus propias cadenas de permiso.
/// </summary>
public static class UserPermission
{
    public const string UserCreate = "USER_CREATE";
    public const string UserEdit = "USER_EDIT";
    public const string UserDelete = "USER_DELETE";
    public const string UserDisable = "USER_DISABLE";
    public const string UserEnable = "USER_ENABLE";
    public const string UserChangeRole = "USER_CHANGE_ROLE";
    public const string UserResetPassword = "USER_RESET_PASSWORD";
    public const string UserForcePasswordChange = "USER_FORCE_PASSWORD_CHANGE";
    public const string UserUnlock = "USER_UNLOCK";
    public const string UserViewAll = "USER_VIEW_ALL";
    public const string UserViewTechnicians = "USER_VIEW_TECHNICIANS";
    public const string UserManageTechnicians = "USER_MANAGE_TECHNICIANS";
    public const string UserViewSelf = "USER_VIEW_SELF";
    public const string UserChangeOwnPassword = "USER_CHANGE_OWN_PASSWORD";

    public const string RoleView = "ROLE_VIEW";
    public const string RoleEdit = "ROLE_EDIT";
    public const string RoleAssign = "ROLE_ASSIGN";
    public const string PermissionView = "PERMISSION_VIEW";
    public const string PermissionEdit = "PERMISSION_EDIT";

    public const string ConnectionSearch = "CONNECTION_SEARCH";
    public const string ConnectionConnect = "CONNECTION_CONNECT";
    public const string ConnectionDisconnect = "CONNECTION_DISCONNECT";
    public const string ConnectionSelectType = "CONNECTION_SELECT_TYPE";
    public const string ConnectionConfigure = "CONNECTION_CONFIGURE";
    public const string ConnectionViewDetails = "CONNECTION_VIEW_DETAILS";

    public const string TestRun = "TEST_RUN";
    public const string TestStop = "TEST_STOP";
    public const string TestReset = "TEST_RESET";
    public const string TestOpenGraphicInterface = "TEST_OPEN_GRAPHIC_INTERFACE";
    public const string TestRunTarjetas = "TEST_RUN_TARJETAS";
    public const string TestRunUnificado = "TEST_RUN_UNIFICADO";
    public const string TestExperimentalEnable = "TEST_EXPERIMENTAL_ENABLE";

    public const string CommandSendPreset = "COMMAND_SEND_PRESET";
    public const string CommandSendManualAllowed = "COMMAND_SEND_MANUAL_ALLOWED";
    public const string CommandSendRaw = "COMMAND_SEND_RAW";
    public const string CommandSendCanDirect = "COMMAND_SEND_CAN_DIRECT";
    public const string CommandSendMaintenance = "COMMAND_SEND_MAINTENANCE";
    public const string CommandScriptUpload = "COMMAND_SCRIPT_UPLOAD";
    public const string HeadProgramSelect = "HEAD_PROGRAM_SELECT";
    public const string ConsoleAdvancedAccess = "CONSOLE_ADVANCED_ACCESS";

    public const string ConfigView = "CONFIG_VIEW";
    public const string ConfigEditBasic = "CONFIG_EDIT_BASIC";
    public const string ConfigEditCritical = "CONFIG_EDIT_CRITICAL";
    public const string ConfigImport = "CONFIG_IMPORT";
    public const string ConfigExport = "CONFIG_EXPORT";
    public const string ConfigValidate = "CONFIG_VALIDATE";
    public const string ConfigRestoreDefaults = "CONFIG_RESTORE_DEFAULTS";

    public const string SystemDevMode = "SYSTEM_DEV_MODE";
    public const string SystemFactoryReset = "SYSTEM_FACTORY_RESET";
    public const string SystemRecovery = "SYSTEM_RECOVERY";
    public const string SystemFileAccess = "SYSTEM_FILE_ACCESS";
    public const string SystemUserRecovery = "SYSTEM_USER_RECOVERY";
    public const string SystemImportConfig = "SYSTEM_IMPORT_CONFIG";
    public const string SystemExportConfig = "SYSTEM_EXPORT_CONFIG";

    public const string FirmwareInfoView = "FIRMWARE_INFO_VIEW";
    public const string FirmwareStatusView = "FIRMWARE_STATUS_VIEW";
    public const string FirmwareUpdatePrepare = "FIRMWARE_UPDATE_PREPARE";
    public const string DeviceInfoView = "DEVICE_INFO_VIEW";

    public const string LogViewSession = "LOG_VIEW_SESSION";
    public const string LogViewAll = "LOG_VIEW_ALL";
    public const string LogExport = "LOG_EXPORT";
    public const string LogClear = "LOG_CLEAR";
    public const string AuditView = "AUDIT_VIEW";
    public const string AuditExport = "AUDIT_EXPORT";
    public const string AuditFilter = "AUDIT_FILTER";
    public const string SessionInvalidate = "SESSION_INVALIDATE";

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta coleccion existe para ofrecer a la app una lista unica con todos los permisos
    /// simbolicos disponibles.
    ///
    /// [QUIÉN LA USA]
    /// La usan `RoleService`, `PermissionService` y validaciones de UI.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al construir roles, grupos de permisos o filtros globales.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la lista completa de permisos conocidos.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Permisos -> `All` -> roles y validaciones.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una mascara completa de capacidades predefinidas.
    ///
    /// [SI NO EXISTIERA]
    /// Cada rol tendria que declarar manualmente su catalogo completo.
    /// </summary>
    public static IReadOnlyCollection<string> All { get; } = new[]
    {
        UserCreate,
        UserEdit,
        UserDelete,
        UserDisable,
        UserEnable,
        UserChangeRole,
        UserResetPassword,
        UserForcePasswordChange,
        UserUnlock,
        UserViewAll,
        UserViewTechnicians,
        UserManageTechnicians,
        UserViewSelf,
        UserChangeOwnPassword,

        RoleView,
        RoleEdit,
        RoleAssign,
        PermissionView,
        PermissionEdit,

        ConnectionSearch,
        ConnectionConnect,
        ConnectionDisconnect,
        ConnectionSelectType,
        ConnectionConfigure,
        ConnectionViewDetails,

        TestRun,
        TestStop,
        TestReset,
        TestOpenGraphicInterface,
        TestRunTarjetas,
        TestRunUnificado,
        TestExperimentalEnable,

        CommandSendPreset,
        CommandSendManualAllowed,
        CommandSendRaw,
        CommandSendCanDirect,
        CommandSendMaintenance,
        CommandScriptUpload,
        HeadProgramSelect,
        ConsoleAdvancedAccess,

        ConfigView,
        ConfigEditBasic,
        ConfigEditCritical,
        ConfigImport,
        ConfigExport,
        ConfigValidate,
        ConfigRestoreDefaults,

        SystemDevMode,
        SystemFactoryReset,
        SystemRecovery,
        SystemFileAccess,
        SystemUserRecovery,
        SystemImportConfig,
        SystemExportConfig,

        FirmwareInfoView,
        FirmwareStatusView,
        FirmwareUpdatePrepare,
        DeviceInfoView,

        LogViewSession,
        LogViewAll,
        LogExport,
        LogClear,
        AuditView,
        AuditExport,
        AuditFilter,
        SessionInvalidate
    };
}
