using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

            _trackingSession.Stop(); 
            _state = TrackerState.STOPPED;
        }

        private TraceLog CreateTraceLog()
        {
            _etlxFileName = TraceLog.CreateFromEventPipeDataFile(_netTraceFileName);
            return new TraceLog(_etlxFileName);
        }

        private MutableTraceEventStackSource GenerateStackSources()
        {
            using (var symbolReader = new SymbolReader(System.IO.TextWriter.Null) { SymbolPath = SymbolPath.MicrosoftSymbolServerPath })
            using (var eventLog = CreateTraceLog())
            {
                var stackSource = new MutableTraceEventStackSource(eventLog)
                {
                    OnlyManagedCodeStacks = true
                };

                var computer = new SampleProfilerThreadTimeComputer(eventLog, symbolReader);
                computer.GenerateThreadTimeStacks(stackSource);
                return stackSource;
            }
        }

        private static Dictionary<StackSourceCallStackIndex, int> CountStackSourceOccurrences(
            MutableTraceEventStackSource stackSource)
        {
            var stackSourceCounter = new DefaultDictionary<StackSourceCallStackIndex, int>();
            stackSource.ForEach(sample => stackSourceCounter[sample.StackIndex] += 1);
            return stackSourceCounter;
        }

        private static void PrintMostCommon(Dictionary<StackSourceCallStackIndex, int> stackSourceCounter, MutableTraceEventStackSource stackSource, int count)
        {
            foreach (var (stackIndex, occurences) in 
                     stackSourceCounter.OrderByDescending(kvp => kvp.Value).Take(count))
            {
                var name = stackSource.GetFrameName(
                    stackSource.GetFrameIndex(stackSource.GetCallerIndex(stackIndex)), true);
                Console.WriteLine($"{name} : {occurences}");
            }
        }
        
        
        private string BuildFullCallStack(StackSourceCallStackIndex stackIndex, MutableTraceEventStackSource stackSource)
        {
            var sb = new StringBuilder();
            while (!stackSource.GetFrameName(stackSource.GetFrameIndex(stackIndex), false).StartsWith("Thread ("))
            {
                var frameName =
                    stackSource.GetFrameName(stackSource.GetFrameIndex(stackIndex), false)
                        .Replace("UNMANAGED_CODE_TIME", "[Native Frames]");
                sb.AppendLine(frameName);
                stackIndex = stackSource.GetCallerIndex(stackIndex);
            }
            return sb.ToString();
        }

        public IEnumerable<(int,string)> Process()
        {
            if (_state != TrackerState.STOPPED)
            {
                throw new Exception($"{nameof(AllocationTracker)} can only process after having stopped!");
            }
            var stackSource = GenerateStackSources();
            var stackSourceCounter = CountStackSourceOccurrences(stackSource);
            PrintMostCommon(stackSourceCounter, stackSource, 10);
            
            foreach (var (stackIndex, count) in 
                     stackSourceCounter.OrderByDescending(kvp => kvp.Value).Take(10))
            {
                yield return (count, BuildFullCallStack(stackIndex, stackSource));
            }
        }

        public IEnumerable<(int, string)> StopAndProcess()
        {
            Stop();
            return Process();
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
