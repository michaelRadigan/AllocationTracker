using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace AllocationTracker
{
    // TODO[michaelr]: This is a bit of a terrible name, please fix
    public class TrackingSession
    {
        private readonly string _netTraceFileName;
        private readonly int _processId;
        private bool _stop;
        
        private Task _sessionTask;
        
        private readonly List<EventPipeProvider> _providers = new List<EventPipeProvider>()
        {
            new EventPipeProvider("Microsoft-Windows-DotNETRuntime",
                EventLevel.Verbose, // this is needed in order to receive AllocationTick_V2 event
                (long) (ClrTraceEventParser.Keywords.GC |
                        // the CLR source code indicates that the provider must be set before the monitored application starts
                        //ClrTraceEventParser.Keywords.GCSampledObjectAllocationLow | 
                        //ClrTraceEventParser.Keywords.GCSampledObjectAllocationHigh | 

                        // required to receive the BulkType events that allows 
                        // mapping between the type ID received in the allocation events
                        ClrTraceEventParser.Keywords.GCHeapAndTypeNames |   
                        ClrTraceEventParser.Keywords.Type |
                        // TODO[michaelr]: Just Experimenting
                        ClrTraceEventParser.Keywords.GCAllObjectAllocation
                )),
        };

        public TrackingSession(int processId, string netTraceFileName)
        {
            _processId = processId;
            _stop = false;
            _netTraceFileName = netTraceFileName;
        }

        public void Start()
        {
            async Task RunEventPipeSession()
            {
                var client = new DiagnosticsClient(_processId);
                using (EventPipeSession session = client.StartEventPipeSession(_providers)) 
                using (FileStream fs = File.OpenWrite(_netTraceFileName))
                {
                    var copyTask = session.EventStream.CopyToAsync(fs);
                    // Keep waiting until we're told to stop
                    while (!_stop)
                    {
                        // TODO[michaelr]: This should probably be configurable in some way!
                        await Task.Delay(1000);
                    }
                    session.Stop();
                    await copyTask;
                }
            }
            //_sessionTask = Task.Factory.StartNew(async () => await RunEventPipeSession());
            _sessionTask = RunEventPipeSession();
        }
        
        /// <summary>
        /// Stop the current tracking session
        /// </summary>
        /// <returns> An awaitable task which is constructing the nettrace file</returns>
        public void Stop()
        {
            _stop = true;
            _sessionTask.Wait();
        }
    }
}