using DocDbClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CosmosStressTester
{
    internal class LoadRunner
    {
        
        DocDbClient.CosmosWriteOperations<PerfTestDto> _dbClient;
        List<Task> _tasks = new List<Task>();
        private Random _random = new Random(DateTime.Now.Millisecond);
        private int _pendingTaskCount;
        private long _itemsInserted;
        private double _fileSize;
        private string _fileSizeString;
        private int READ_RU = 1;
        private int WRITE_RU = 5;
        

        private double[] RequestUnitsConsumed { get; set; }
        

        public LoadRunner(DocDbClient.CosmosWriteOperations<PerfTestDto> dbClient)
        {
            _dbClient = dbClient;
            this._pendingTaskCount = Config.Concurrency;
        }

        public async Task Run()
        {
            Logger.Info($"Starting a load run with a concurrency of {Config.Concurrency} and number of records: {Config.Records}");
            this.RequestUnitsConsumed = new double[Config.Concurrency];

            // Task based execution
            for (var thrd = 0; thrd < Config.Concurrency; thrd++)
            {
                this.RequestUnitsConsumed[thrd] = 0;

                var storeNumber = System.Threading.Thread.CurrentThread.ManagedThreadId;
                var testObj = new PerfTestDto
                {
                    StoreNumber = storeNumber,
                    Key = thrd.ToString(),
                    Value = RandomString(1023768)
                };
                this._fileSize = Size(testObj);
                this._fileSizeString = SizeString(testObj);
                if (this._fileSize > 90000 && this._fileSize < 110000)
                {
                    READ_RU = READ_RU * 10;
                    WRITE_RU = WRITE_RU * 10;
                }

                if (Config.ReadWrite == "W")
                {
                    _tasks.Add(WriteDto(thrd, testObj));
                }
                else
                {
                    _tasks.Add(ReadDto(thrd));
                }
            }
            _tasks.Add(LogOutputStats());

            await Task.WhenAll(_tasks); // run

            // clean up
            if(Config.CleanupOnFinish)
                this.Cleanup();
        }

        private async Task WriteDto(int taskId, PerfTestDto testObj)
        {
            for (var cnt = 0; cnt < Config.Records; cnt++)
            {
                await _dbClient.AddDocumentInCollection(testObj);

                this.RequestUnitsConsumed[taskId] += WRITE_RU;
                Interlocked.Increment(ref this._itemsInserted);
            }
            Interlocked.Decrement(ref this._pendingTaskCount);
        }

        private async Task ReadDto(int taskId)
        {
            for (var cnt = 0; cnt < Config.Records; cnt++)
            {
                await _dbClient.GetDocumentInCollection(cnt.ToString());

                this.RequestUnitsConsumed[taskId] += READ_RU;
                Interlocked.Increment(ref this._itemsInserted);
            }
            Interlocked.Decrement(ref this._pendingTaskCount);
        }

        private void Cleanup()
        {
            Logger.Info("Clean up");
            _dbClient.CleanupDocumentCollection();
        }

        private async Task LogOutputStats()
        {
            long lastCount = 0;
            double lastRequestUnits = 0;
            double lastSeconds = 0;
            double requestUnits = 0;
            double ruPerSecond = 0;
            double ruPerMonth = 0;
            var output = new StringBuilder();

            Stopwatch watch = new Stopwatch();
            watch.Start();

            while (this._pendingTaskCount > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                double seconds = watch.Elapsed.TotalSeconds;

                requestUnits = this.RequestUnitsConsumed.Sum();

                ruPerSecond = (requestUnits / seconds);
                ruPerMonth = ruPerSecond * 86400 * 30;
                string msg;
                if (Config.ReadWrite == "W")
                {
                    msg = String.Format("Inserted {0} docs @ {1} writes/s, {2} RU/s ({3}B max monthly {4} reads) second {5}",
                        this._itemsInserted,
                        Math.Round(this._itemsInserted / seconds),
                        Math.Round(ruPerSecond),
                        Math.Round(ruPerMonth / (1000 * 1000 * 1000)),
                        _fileSizeString,
                        seconds);
                }
                else
                {
                    msg = String.Format("Get {0} docs @ {1} reads/s, {2} RU/s ({3}B max monthly {4} reads) second {5}",
                        this._itemsInserted,
                        Math.Round(this._itemsInserted / seconds),
                        Math.Round(ruPerSecond),
                        Math.Round(ruPerMonth / (1000 * 1000 * 1000)),
                        _fileSizeString,
                        seconds);
                }

                Logger.Info(msg);
                output.AppendLine(msg);

                lastCount = _itemsInserted;
            }

            double totalSeconds = watch.Elapsed.TotalSeconds;
            ruPerSecond = (requestUnits / totalSeconds);
            ruPerMonth = ruPerSecond * 86400 * 30;
            string msg1;
            if (Config.ReadWrite == "W")
            {
                msg1 = String.Format("Inserted {0} items @ {1} writes/s, {2} RU/s ({3}B max monthly {4} reads) second {5}",
                    lastCount,
                    Math.Round(this._itemsInserted / watch.Elapsed.TotalSeconds),
                    Math.Round(ruPerSecond),
                    Math.Round(ruPerMonth / (1000 * 1000 * 1000)),
                    _fileSizeString,
                    totalSeconds);
            }
            else
            {
                msg1 = String.Format("Get {0} items @ {1} reads/s, {2} RU/s ({3}B max monthly {4} reads) second {5}",
                    lastCount,
                    Math.Round(this._itemsInserted / watch.Elapsed.TotalSeconds),
                    Math.Round(ruPerSecond),
                    Math.Round(ruPerMonth / (1000 * 1000 * 1000)),
                    _fileSizeString,
                    totalSeconds);
            }

            Logger.Info("Summary:");
            Logger.Info("--------------------------------------------------------------------- ");
            Logger.Info(msg1);
            Logger.Info("--------------------------------------------------------------------- ");
            output.AppendLine();
            output.AppendLine("Summary:");
            output.AppendLine("--------------------------------------------------------------------- ");
            output.AppendLine(msg1);
            output.AppendLine("--------------------------------------------------------------------- ");

            System.IO.File.WriteAllText("RunResults.txt", output.ToString());

        }

        private double Size(object obj)
        {
            MemoryStream m = new MemoryStream();

            BinaryFormatter b = new BinaryFormatter();
            b.Serialize(m, obj);
            return Convert.ToDouble(m.Length);
        }

        private string SizeString(object obj)
        {
            double size = Size(obj);
            return ByteFormat(size);
        }

        private string ByteFormat(double file)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = file;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return String.Format("{0:0.##}{1}", len, sizes[order]);
        }

        public string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[_random.Next(s.Length)]).ToArray());
        }
    }
}
