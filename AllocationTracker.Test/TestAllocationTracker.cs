using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace AllocationTracker.Test
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void MessyTest()
        {
            var arr = new int[100];
            var allocationTracker = new AllocationTracker();
            allocationTracker.Start();

            for (var i = 0; i < 30; i++)
            {
                Allocate10K();
                Allocate5K();
                GC.Collect();
            }

            allocationTracker.StopAndProcess();
            // TODO[michaelr]: Probably want a better return type than just printing to console...
        }
        
        [Test]
        public void SimpleTest()
        {
            var allocationTracker = new AllocationTracker();
            allocationTracker.Start();
            
            Allocate10K();
            Allocate5K();

            allocationTracker.StopAndProcess();
            // TODO[michaelr]: Probably want a better return type than just printing to console...
        }
        
        
        private static void Allocate10K()
        {
            for (int i = 0; i < 10000; i++)
            {
                int[] x = new int[100];
            }
        }

        private static void Allocate5K()
        {
            for (int i = 0; i < 5000; i++)
            {
                int[] x = new int[100];
            }
        }

        [Test]
        public void FinalizerTest()
        {
            var allocationTracker = new AllocationTracker();
            allocationTracker.Start();

            for (var i = 0; i < 150; i++)
            {
                GC.Collect();
            }

            allocationTracker.StopAndProcess();
            // TODO[michaelr]: Probably want a better return type than just printing to console...
        }
    }
}