namespace CodeGeneration;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length != 5)
        {
            Console.WriteLine("not enough arguments: <input files separated with \"|\"> <output directory> " +
                              "<degree of reading parallelism> <degree of generation parallelism > <degree of writing parallelism>");
            return;
        }

        var inputFiles = args[0].Split('|');
        var correctInputFiles = new List<string>();
        foreach (var inputFile in inputFiles)
        {
            if (File.Exists(inputFile))
            {
                correctInputFiles.Add(inputFile);
            }
            else
            {
                Console.WriteLine($"file {inputFile} doesn't exist or you don't have a permission to open it. it is removed from generation");
            }
        }

        var outputDir = args[1];

        if (!int.TryParse(args[2], out var degreeOfReadingParallelism))
        {
            Console.WriteLine($"invalid degree of reading parallelism. integer expected, not {args[2]}");
            return;
        }
        if (!int.TryParse(args[3], out var degreeOfGenerationParallelism))
        {
            Console.WriteLine($"invalid degree of generation parallelism. integer expected, not {args[3]}");
            return;
        }
        if (!int.TryParse(args[4], out var degreeOfWritingParallelism))
        {
            Console.WriteLine($"invalid degree of writing parallelism. integer expected, not {args[4]}");
            return;
        }

        var Generation = new Generation(degreeOfReadingParallelism, degreeOfGenerationParallelism,
            degreeOfWritingParallelism, outputDir);
        await Generation.Generate(correctInputFiles);
    }
}
