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
    public class TrackingSession
    {
        private readonly string _netTraceFileName;
        private readonly int _processId;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private Task _sessionTask;
        
        private readonly List<EventPipeProvider> _providers = new List<EventPipeProvider>()
        {
            new EventPipeProvider("Microsoft-Windows-DotNETRuntime",
                EventLevel.Verbose, // this is needed in order to receive AllocationTick_V2 event
                (long) (ClrTraceEventParser.Keywords.GC |
                        // required to receive the BulkType events that allows 
                        // mapping between the type ID received in the allocation events
                        ClrTraceEventParser.Keywords.GCHeapAndTypeNames |   
                        ClrTraceEventParser.Keywords.Type |
                        // TODO[michaelr]: Just Experimenting
                        // the CLR source code indicates that the provider must be set before the monitored application starts
                        ClrTraceEventParser.Keywords.GCAllObjectAllocation
                )),
        };

        public TrackingSession(int processId, string netTraceFileName)
        {
            _processId = processId;
            _netTraceFileName = netTraceFileName;
            _cancellationTokenSource = new CancellationTokenSource();
        }
        
        
        /// <summary>
        /// Start the current tracking session
        /// </summary>
        public void Start()
        {
            async Task RunEventPipeSession(CancellationToken token)
            {
                var client = new DiagnosticsClient(_processId);
                var session = client.StartEventPipeSession(_providers);
                using (FileStream fs = File.OpenWrite(_netTraceFileName))
                {
                    // Note that we're intentionally not propagating through the cancellation token, we would always like the full copy to complete.
                    var copyTask = session.EventStream.CopyToAsync(fs);
                    token.Register(() =>
                    {
                        session?.Stop();
                        session?.Dispose();
                    });
                    await copyTask;
                }
            }
            _sessionTask = RunEventPipeSession(_cancellationTokenSource.Token);
        }
        
        /// <summary>
        /// Stop the current tracking session
        /// </summary>
        public void Stop()
        {
            _cancellationTokenSource.Cancel();
            _sessionTask.Wait();
        }
    }
}