// [ACURATEX] Estado minimo del formulario de login embebido.
namespace AcuratexControlApp.Components;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para guardar el estado visual minimo del formulario de login.
///
/// [QUIEN LA USA]
/// La usa `LoginForm` y `LoginView`.
///
/// [CUANDO SE USA]
/// Se usa mientras el usuario escribe credenciales o aparece un error.
///
/// [ENTRADAS]
/// Recibe mensajes de error desde la logica de login.
///
/// [SALIDAS]
/// Devuelve un estado simple para la vista.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// LoginForm -> `LoginViewState` -> LoginView.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una variable de estado que guarda si hay error y que texto mostrar.
///
/// [SI NO EXISTIERA]
/// La vista tendria que guardar su propio estado de error sin separarlo de la logica.
/// </summary>
public sealed class LoginViewState
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para guardar el texto exacto del error visible en la pantalla de login.
    ///
    /// [QUIÉN LA USA]
    /// La usa la vista `LoginView` para decidir qué mensaje dibujar debajo de los campos.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando la validación falla, cuando el acceso es rechazado o cuando hay un problema de sesión.
    ///
    /// [ENTRADAS]
    /// Recibe texto preparado por la lógica de autenticación.
    ///
    /// [SALIDAS]
    /// Devuelve a la UI el mensaje que debe mostrar al usuario.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Al asignarse, cambia directamente lo que verá la pantalla de login.
    ///
    /// [FLUJO ACURATEX]
    /// LoginForm -> estado de vista -> LoginView -> mensaje en pantalla.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un registro temporal de estado que guarda el último motivo de fallo.
    ///
    /// [SI NO EXISTIERA]
    /// La vista tendría que construir el mensaje por su cuenta y la lógica quedaría mezclada con la presentación.
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para responder con una pregunta simple: "¿hay un error visible ahora?".
    ///
    /// [QUIÉN LA USA]
    /// La usa la vista de login antes de decidir si muestra o esconde el bloque de error.
    ///
    /// [CUÁNDO SE USA]
    /// Se evalúa cada vez que Blazor vuelve a renderizar el formulario.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros; calcula su resultado a partir de `ErrorMessage`.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si el texto no está vacío y `false` si no hay nada que mostrar.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado; solo lee el texto actual.
    ///
    /// [FLUJO ACURATEX]
    /// LoginView -> `HasError` -> decisión visual de la UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bit de bandera que indica si una condición está activa.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz tendría que repetir la misma comprobación de texto vacío en varios lugares.
    /// </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
}
