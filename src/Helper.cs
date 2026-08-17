public class Helper 
{
    public static int mode = 1;
    public static string jsonPath = "";

    public static Version version = new Version(1, 1, 0);

    public static void Flags(string[] args) {
        if (args.Length >= 1) {
            for (int i = 0; i < args.Length; i++) {
                if (args[i] == "--json") {
                    if (i + 1 < args.Length) {
                        jsonPath = args[i + 1];
                        i += 1;
                    }

                    mode = 2;
                }
                if (args[i] == "-h" || args[i] == "--help") {
                    Console.WriteLine("NES Help:");
                    Console.WriteLine("--nes <string:rom>: Start up the emulator with given ROM passed in. Consider as mode 1");
                    Console.WriteLine("--json <string:json>: Runs the JSON Test. The tests must be in \"test\\v1\". Consider as mode 2");
                    Console.WriteLine("-s <int>, --scale <int>: Scale window size by factor (2 is default)");
                    Console.WriteLine("-f, --fps: Enables FPS counter (off is default)");
                    Console.WriteLine("-rl, --raylib-log: Enables Raylib logs (off is default)");
                    Console.WriteLine("-h, --help: Show help screen (What you are seeing now)");
                    Console.WriteLine("Controls: (A)=X, (B)=Z, D-Pad=ArrowKeys, [START]=ENTER, [SELECT]=SHIFT");
                    Console.WriteLine("In debug mode: Press [SPACE] to toggle Sprite0 Hit Check. Try this out if a game freezes");
                    Console.WriteLine("ROMs must have a iNES header!");
                    Console.WriteLine("In GUI, look at Help -> Manual");
                    Environment.Exit(1);
                }
            }

            if (mode == 0) {
                Console.WriteLine("Error: No mode passed in");
                Console.WriteLine("Mode: --nes <string:rom> or --json <string:json>");
                Console.WriteLine("Use -h or --help to bring up help options.");
                Environment.Exit(1);
            }
        } else {
            Console.WriteLine("To get started, use -h or --help to bring up help options.");
            Console.WriteLine("Or use the GUI. \"Help -> Manual\"");
            //Environment.Exit(1);
        }
        Console.WriteLine();
    }

    public static void ASCII_NES() {
        Console.WriteLine(" ____________________________ ");
        Console.WriteLine("│ │  NES               │---│ │");
        Console.WriteLine("│ │____________________│___│ │");
        Console.WriteLine("│____________________________│");
        Console.WriteLine("|                     1  2   |");
        Console.WriteLine(" \\ ■ [ ] [ ]          ▒  ▒  / ");
        Console.WriteLine("  ∙------------------------∙  ");
    }
}