// [ACURATEX] Este archivo define el primer punto de ejecución de toda la app WinForms.
// Aquí no hay lógica de negocio: solo arranque, configuración base y transición visual
// desde la pantalla splash hacia la ventana principal.
namespace AcuratexControlApp;

// [C#] `static class` significa que esta clase no se instancia.
// [C#] En C# una clase estática solo puede contener miembros estáticos.
static class Program
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para iniciar la aplicación WinForms desde el sistema operativo.
    /// Su trabajo es preparar el entorno gráfico, mostrar el splash y luego entregar el
    /// control a la ventana principal.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama Windows cuando el ejecutable arranca. No la llama otra función de la app.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta una sola vez, al inicio del proceso.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Inicializa la configuración de la aplicación, abre el splash y arranca el bucle
    /// de eventos de Windows con `Form1`.
    ///
    /// [FLUJO ACURATEX]
    /// Windows -> Program.Main -> SplashForm -> Form1 -> espera de eventos.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al `setup()` de Arduino: prepara recursos antes de entrar al ciclo
    /// principal de atención a eventos.
    ///
    /// [SI NO EXISTIERA]
    /// El ejecutable no tendría un punto inicial claro y la aplicación no podría arrancar.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // [C#] `[STAThread]` indica que este hilo principal debe usar un modelo STA.
        // [ACURATEX] WinForms y algunos controles COM/Windows exigen este contexto.
        // [FLUJO] Antes de mostrar cualquier ventana, se fija la configuración base del
        // entorno gráfico.
        ApplicationConfiguration.Initialize();

        // [C#] `using SplashForm splash = new();` crea el objeto y garantiza su liberación
        // automática al salir del bloque.
        // [ACURATEX] El splash es una pantalla temporal para dar feedback visual mientras
        // la aplicación principal termina de arrancar.
        using SplashForm splash = new();

        // [FLUJO] Muestra el splash como diálogo modal.
        // Esto bloquea aquí hasta que el splash se cierra.
        splash.ShowDialog();

        // [C#] `Application.Run(...)` entrega el control al bucle de mensajes de Windows.
        // [ACURATEX] Aquí empieza la vida real de la ventana principal.
        // No es un `while(true)` visible en el código, pero sí equivale a quedar esperando
        // eventos de teclado, mouse, timers y mensajes del sistema.
        Application.Run(new Form1());
    }
}
