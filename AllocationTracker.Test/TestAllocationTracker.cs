using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers.MicrosoftWindowsWPF;
using NUnit.Framework;

namespace AllocationTracker.Test
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        private void PrintProcessed(IEnumerable<(int, string)> processedStacks)
        {
            foreach (var (count, stack) in processedStacks)
            {
                Console.WriteLine($"{count} times: ");    
                Console.WriteLine(stack);
            }
        }

        [Test]
        public void MessyTest()
        {
            var allocationTracker = new AllocationTracker();
            allocationTracker.Start();

            for (var i = 0; i < 30; i++)
            {
                Allocate10K();
                Allocate5K();
                GC.Collect();
            }

            var countsAndStacks = allocationTracker.StopAndProcess();
            PrintProcessed(countsAndStacks);
        }

        [Test]
        public void SimpleTest()
        {
            var allocationTracker = new AllocationTracker();
            allocationTracker.Start();
            
            Allocate10K();
            Allocate5K();

            var countsAndStacks = allocationTracker.StopAndProcess();
            PrintProcessed(countsAndStacks);
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