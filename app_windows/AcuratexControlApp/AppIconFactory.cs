// [ACURATEX] Esta clase crea el icono principal de la app en memoria.
// No lee archivos de imagen: dibuja el icono con GDI+ y luego lo entrega a WinForms.
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AcuratexControlApp;

// [C#] `internal` limita el uso de la clase al ensamblado actual.
// [C#] `static` significa que no se crean objetos de esta clase.
internal static class AppIconFactory
{
    // [C#] `DllImport` enlaza una función de una DLL nativa de Windows.
    // [ACURATEX] `DestroyIcon` libera el handle nativo para no filtrar recursos del sistema.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para crear un icono visual coherente con la marca de la app.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `Form1` al iniciar, antes de mostrar la ventana principal.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta una vez al arrancar la interfaz, o cada vez que se necesite reconstruir
    /// el icono.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// Devuelve un objeto `Icon` listo para asignarse a una ventana.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Crea recursos gráficos temporales y libera el handle nativo del icono.
    ///
    /// [FLUJO ACURATEX]
    /// Form1 -> AppIconFactory.CreateIcon() -> GDI+ dibuja -> WinForms recibe el icono.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a generar una señal o patrón visual en tiempo de arranque para identificar
    /// el equipo.
    ///
    /// [SI NO EXISTIERA]
    /// La ventana usaría un icono vacío o el predeterminado del sistema.
    /// </summary>
    public static Icon CreateIcon()
    {
        // [C#] `using` con `Bitmap` y `Graphics` asegura liberación automática.
        // [ACURATEX] Se dibuja el icono directamente en memoria.
        using Bitmap bitmap = new(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        // [C#] `SmoothingMode` controla la calidad de bordes al dibujar.
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        // [ACURATEX] La geometría externa del icono: un rectángulo redondeado.
        RectangleF outerBounds = new(5, 5, 54, 54);
        // [C#] `GraphicsPath` guarda la ruta vectorial que después se rellena y se dibuja.
        using GraphicsPath outerPath = CreateRoundedRectanglePath(outerBounds, 14);
        // [C#] `LinearGradientBrush` crea un relleno con degradado.
        // [ACURATEX] El degradado da identidad visual sin depender de una imagen externa.
        using LinearGradientBrush backgroundBrush = new(
            outerBounds,
            Color.FromArgb(56, 189, 248),
            Color.FromArgb(34, 197, 94),
            135f);
        graphics.FillPath(backgroundBrush, outerPath);

        // [ACURATEX] El borde brillante refuerza el contorno del icono.
        using Pen glowPen = new(Color.FromArgb(170, 219, 234, 254), 2.4f);
        graphics.DrawPath(glowPen, outerPath);

        // [ACURATEX] El rombo central actúa como marca simple dentro del icono.
        PointF[] diamond = {
            new(32, 18),
            new(46, 32),
            new(32, 46),
            new(18, 32),
        };
        // [C#] `SolidBrush` rellena una figura con un solo color.
        using SolidBrush diamondBrush = new(Color.FromArgb(214, 5, 11, 20));
        graphics.FillPolygon(diamondBrush, diamond);

        // [C#] `GetHicon()` crea un handle nativo de icono desde el bitmap.
        IntPtr iconHandle = bitmap.GetHicon();
        try {
            // [C#] `Icon.FromHandle()` envuelve el handle nativo en un objeto administrado.
            // [C#] `Clone()` crea una copia independiente para que el icono siga siendo válido
            // cuando el handle original se libere.
            using Icon icon = Icon.FromHandle(iconHandle);
            return (Icon)icon.Clone();
        } finally {
            // [ACURATEX] Liberación explícita del recurso nativo de Windows.
            DestroyIcon(iconHandle);
        }
    }

    // [C#] Método auxiliar `private` porque solo sirve para construir la forma del icono.
    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para construir la ruta vectorial de un rectangulo redondeado.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama `CreateIcon()`, que necesita un contorno geometrico antes de dibujar.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta durante la construccion del icono principal.
    ///
    /// [ENTRADAS]
    /// Recibe el rectangulo base y el radio de las esquinas.
    ///
    /// [SALIDAS]
    /// Devuelve un `GraphicsPath` listo para rellenar o dibujar.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Crea una ruta grafica en memoria.
    ///
    /// [FLUJO ACURATEX]
    /// `CreateIcon()` -> `CreateRoundedRectanglePath()` -> geometria del icono.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a calcular la geometria previa de una figura antes de pintarla en pantalla.
    ///
    /// [SI NO EXISTIERA]
    /// El icono tendria que dibujarse con una forma repetida o menos clara.
    /// </summary>
    private static GraphicsPath CreateRoundedRectanglePath(RectangleF bounds, float radius)
    {
        float diameter = radius * 2f;
        // [C#] `new()` usa inferencia de tipo a partir del lado izquierdo.
        GraphicsPath path = new();
        // [ACURATEX] Cada arco forma una esquina redondeada del contorno.
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
