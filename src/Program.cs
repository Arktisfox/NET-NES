using Emulator;

public class Program {
    public static void Main(string[] args) {
        Console.WriteLine("NET-NES");
        Helper.Flags(args);

        if (Helper.mode == 1) {
           GUI gui = new GUI(args);

           gui.Run();
        } else if (Helper.mode == 2) {
            TestRunner testRunner = new TestRunner();
            
            testRunner.Run(Helper.jsonPath);
        }
    }
}