using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace AllocationTracker
{
    public class AllocationTracker : IDisposable
    {
        private readonly int _processId;
        private readonly string _netTraceFileName;
        private string _etlxFileName;
        private TrackingSession _trackingSession;
        private TrackerState _state;

        /// <summary>
        /// Tracks allocation information sent by the dotnet runtime for the given dotnet process.
        /// </summary>
        /// <param name="processId">The processID of the dotnet process that should be tracked</param>
        /// <param name="netTraceFileName">The net trace file to write</param>
        public AllocationTracker(int processId, string netTraceFileName=null)
        {
            _processId = processId;
            _netTraceFileName ??= Path.GetRandomFileName() + ".nettrace";
            _etlxFileName = null;
            _state = TrackerState.INITIAL;
        }
        
        public AllocationTracker() : this(Environment.ProcessId)
        {
        }

        public void Start()
        {
            if (_state != TrackerState.INITIAL)
            {
                throw new Exception($"{nameof(AllocationTracker)} may not be started more than once!");
            }
            _state = TrackerState.STARTED;
            _trackingSession = new TrackingSession(_processId, _netTraceFileName);
            _trackingSession.Start();
        }

        public void Stop()
        {
            if (_state != TrackerState.STARTED)
            {
                throw new Exception($"{nameof(AllocationTracker)} can only be stopped if it has already been started!");
            }

            Console.WriteLine($"{nameof(AllocationTracker)}: pre");
            _trackingSession.Stop();  //.ContinueWith(_ => _state = TrackerState.STOPPED);
            _state = TrackerState.STOPPED;
        }

        private TraceLog CreateTraceLog()
        {
            _etlxFileName = TraceLog.CreateFromEventPipeDataFile(_netTraceFileName);
            return new TraceLog(_etlxFileName);
        }

        // TODO[michaelr]: Clean this up, like, a lot
        public void Process()
        {
            if (_state != TrackerState.STOPPED)
            {
                throw new Exception($"{nameof(AllocationTracker)} can only process after having stopped!");
            }
            
            using (var symbolReader = new SymbolReader(System.IO.TextWriter.Null) { SymbolPath = SymbolPath.MicrosoftSymbolServerPath })
            using (var eventLog = CreateTraceLog())
            {
                var stackSource = new MutableTraceEventStackSource(eventLog)
                {
                    OnlyManagedCodeStacks = true
                };

                var computer = new SampleProfilerThreadTimeComputer(eventLog, symbolReader);
                computer.GenerateThreadTimeStacks(stackSource);

                var samplesForThread = new Dictionary<int, List<StackSourceSample>>();

                stackSource.ForEach((sample) =>
                {
                    var stackIndex = sample.StackIndex;
                    while (!stackSource.GetFrameName(stackSource.GetFrameIndex(stackIndex), false)
                               .StartsWith("Thread ("))
                        stackIndex = stackSource.GetCallerIndex(stackIndex);

                    // long form for: int.Parse(threadFrame["Thread (".Length..^1)])
                    // Thread id is in the frame name as "Thread (<ID>)"
                    string template = "Thread (";
                    string threadFrame = stackSource.GetFrameName(stackSource.GetFrameIndex(stackIndex), false);
                    int threadId =
                        int.Parse(threadFrame.Substring(template.Length, threadFrame.Length - (template.Length + 1)));

                    if (samplesForThread.TryGetValue(threadId, out var samples))
                    {
                        samples.Add(sample);
                    }
                    else
                    {
                        samplesForThread[threadId] = new List<StackSourceSample>() {sample};
                    }
                });

                var counter = new Dictionary<Tuple<int, StackSourceCallStackIndex>, int>();

                foreach (var (threadId, samples) in samplesForThread)
                {
                    // Why are we only printing the first??
                    foreach (var sample in samples)
                    {
                        var key = new Tuple<int, StackSourceCallStackIndex>(threadId, sample.StackIndex);
                        if (counter.TryGetValue(key, out var count))
                        {
                            counter[key] = count + 1;
                        }
                        else
                        {
                            counter[key] = 1;
                        }
                        //PrintStack(threadId, sample, stackSource);
                    }
                    //PrintStack(threadId, samples[0], stackSource);
                }

                foreach (var ((threadId, stackIndex), count) in counter.OrderBy(kvp => kvp.Value).Take(10))
                {
                    var name = stackSource.GetFrameName(
                        stackSource.GetFrameIndex(stackSource.GetCallerIndex(stackIndex)), true);
                    Console.WriteLine($"{name} : {count}");
                }
            }
        }

        public void StopAndProcess()
        {
            Stop();
            Process();
        }

        public void Dispose()
        {
            if (File.Exists(_netTraceFileName))
                File.Delete(_netTraceFileName);
            if (File.Exists(_etlxFileName))
                File.Delete(_etlxFileName);
        }
    }
}
