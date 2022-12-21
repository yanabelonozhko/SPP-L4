using System.Threading.Tasks.Dataflow;

namespace CodeGeneration
{
    public class Generation
    {
        private readonly Generator.Generator _testsGenerator = new();

        private TransformBlock<string, string> _readerBlock;
        private TransformManyBlock<string, Generator.Generator.ClassInfo> _generatorBlock;
        private ActionBlock<Generator.Generator.ClassInfo> _writerBlock;

        public string SavePath { get; set; }
        public int DegreeOfReadingParallelism { get; }
        public int DegreeOfGenerationParallelism { get; }
        public int DegreeOfWritingParallelism { get; }

        public Generation(int degreeOfParallelismRead, int degreeOfParallelismGenerate,
            int degreeOfParallelismWrite, string savePath = "")
        {
            DegreeOfReadingParallelism = degreeOfParallelismRead;
            DegreeOfGenerationParallelism = degreeOfParallelismGenerate;
            DegreeOfWritingParallelism = degreeOfParallelismWrite;

            SavePath = savePath;

            _readerBlock = new TransformBlock<string, string>(async fileName =>
            {
                using var reader = File.OpenText(fileName);
                return await reader.ReadToEndAsync();
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = degreeOfParallelismRead });
            _generatorBlock =
                new TransformManyBlock<string, Generator.Generator.ClassInfo>(source =>
                        _testsGenerator.Generate(source),
                    new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = degreeOfParallelismGenerate });
            _writerBlock = new ActionBlock<Generator.Generator.ClassInfo>(async classInfo =>
            {
                await using var writer = new StreamWriter(SavePath + "\\" + classInfo.ClassName + ".cs");
                await writer.WriteAsync(classInfo.TestsFile);
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = degreeOfParallelismWrite });

            _readerBlock.LinkTo(_generatorBlock, new DataflowLinkOptions { PropagateCompletion = true });
            _generatorBlock.LinkTo(_writerBlock, new DataflowLinkOptions { PropagateCompletion = true });
        }

        public async Task Generate(List<string> fileNames)
        {
            foreach (var fileName in fileNames)
            {
                _readerBlock.Post(fileName);
            }

            _readerBlock.Complete();
            await _writerBlock.Completion;
        }
    }
}
