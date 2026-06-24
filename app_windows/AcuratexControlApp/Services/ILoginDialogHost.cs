// [ACURATEX] Este contrato permite que la vista de login le pida trabajo a la ventana WinForms
// sin conocer la clase concreta que la contiene.
using AcuratexControlApp.Components;

namespace AcuratexControlApp.Services;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta interfaz existe para que la pantalla de login pueda validar credenciales y notificar
/// cambios de estado sin depender del formulario concreto.
///
/// [QUIÉN LA USA]
/// La usa `LoginView` desde Razor.
///
/// [CUÁNDO SE USA]
/// Se usa cuando el usuario escribe credenciales o cuando la vista necesita repintarse.
///
/// [ENTRADAS]
/// Recibe usuario y contraseña al intentar iniciar sesión.
///
/// [SALIDAS]
/// Expone el estado visual del login y un método asincrónico de envío.
///
/// [EFECTOS SECUNDARIOS]
/// Puede cambiar el mensaje de error y cerrar la ventana si el login es válido.
///
/// [FLUJO ACURATEX]
/// LoginView -> ILoginDialogHost -> LoginForm -> AuthStateService.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un callback de autenticación que confirma o niega acceso.
///
/// [SI NO EXISTIERA]
/// La vista tendría que conocer la ventana concreta y el login perdería encapsulación.
/// </summary>
public interface ILoginDialogHost
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para que la vista lea errores, usuario y flags del login.
    ///
    /// [QUIÉN LA USA]
    /// La usa `LoginView`.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta durante el render.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el estado actual del diálogo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// LoginForm -> State -> Razor.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer un registro de estado de acceso.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabría qué credenciales mostrar o qué error enseñar.
    /// </summary>
    // [C#] La propiedad de solo lectura deja que la vista observe el estado sin reemplazarlo.
    LoginViewState State { get; }
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este evento existe para avisar a la vista de login que debe repintarse.
    ///
    /// [QUIÉN LO USA]
    /// Lo suscribe `LoginView`.
    ///
    /// [CUÁNDO SE USA]
    /// Se dispara cuando cambia el estado de error o cuando el login termina.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Produce actualización visual.
    ///
    /// [FLUJO ACURATEX]
    /// LoginForm -> StateChanged -> Razor.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una notificación de refresco de pantalla.
    ///
    /// [SI NO EXISTIERA]
    /// El error de login podría no mostrarse a tiempo.
    /// </summary>
    // [C#] `event` permite que la vista se suscriba al cambio y luego se desuscriba.
    event Action? StateChanged;
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este método existe para enviar credenciales a la lógica de autenticación.
    ///
    /// [QUIÉN LO USA]
    /// Lo llama la vista Razor cuando el usuario confirma el formulario.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta en cada intento de inicio de sesión.
    ///
    /// [ENTRADAS]
    /// Recibe usuario y contraseña.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede modificar el estado del login o cerrar el diálogo.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `SubmitAsync()` -> AuthStateService -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a validar un PIN antes de permitir entrar a una consola.
    ///
    /// [SI NO EXISTIERA]
    /// La vista no tendría una forma estándar de pedir validación.
    /// </summary>
    // [C#] `Task` deja que la vista espere el resultado de la validacion sin bloquear el hilo.
    Task SubmitAsync(string userId, string password);
}
