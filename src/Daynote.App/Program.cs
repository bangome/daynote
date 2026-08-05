namespace Daynote.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var options = Showcase.ShowcaseOptions.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(Showcase.ShowcaseOptions.Usage);
                return 0;
            }

            if (options.Showcase)
            {
                return Showcase.ShowcaseApplication.Run(options);
            }

            return RunProduct();
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(Showcase.ShowcaseOptions.Usage);
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int RunProduct()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
