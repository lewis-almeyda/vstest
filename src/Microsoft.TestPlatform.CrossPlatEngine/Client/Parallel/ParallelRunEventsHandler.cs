// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.Parallel
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;

    using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
    using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.Interfaces;
    using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.ObjectModel;
    using Microsoft.VisualStudio.TestPlatform.ObjectModel;
    using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
    using Microsoft.VisualStudio.TestPlatform.ObjectModel.Engine;
    using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

    /// <summary>
    /// ParallelRunEventsHandler for handling the run events in case of parallel execution
    /// </summary>
    internal class ParallelRunEventsHandler : ITestRunEventsHandler
    {
        private IProxyExecutionManager proxyExecutionManager;

        private ITestRunEventsHandler actualRunEventsHandler;

        private IParallelProxyExecutionManager parallelProxyExecutionManager;

        private ParallelRunDataAggregator runDataAggregator;

        private IDataSerializer dataSerializer;

        public ParallelRunEventsHandler(IProxyExecutionManager proxyExecutionManager,
            ITestRunEventsHandler actualRunEventsHandler,
            IParallelProxyExecutionManager parallelProxyExecutionManager,
            ParallelRunDataAggregator runDataAggregator) : 
            this(proxyExecutionManager, actualRunEventsHandler, parallelProxyExecutionManager, runDataAggregator, JsonDataSerializer.Instance)
        {
        }


        internal ParallelRunEventsHandler(IProxyExecutionManager proxyExecutionManager,
            ITestRunEventsHandler actualRunEventsHandler,
            IParallelProxyExecutionManager parallelProxyExecutionManager,
            ParallelRunDataAggregator runDataAggregator,
            IDataSerializer dataSerializer)
        {
            this.proxyExecutionManager = proxyExecutionManager;
            this.actualRunEventsHandler = actualRunEventsHandler;
            this.parallelProxyExecutionManager = parallelProxyExecutionManager;
            this.runDataAggregator = runDataAggregator;
            this.dataSerializer = dataSerializer;
        }

        /// <summary>
        /// Handles the Run Complete event from a parallel proxy manager
        /// </summary>
        public void HandleTestRunComplete(
            TestRunCompleteEventArgs testRunCompleteArgs,
            TestRunChangedEventArgs lastChunkArgs,
            ICollection<AttachmentSet> runContextAttachments,
            ICollection<string> executorUris)
        {
            // we get run complete events from each executor process
            // so we cannot "complete" the actual executor operation until all sources/testcases are consumed
            // We should not block last chunk results while we aggregate overall run data 
            if (lastChunkArgs != null)
            {
                ConvertToRawMessageAndSend(MessageType.TestRunStatsChange, lastChunkArgs);
                HandleTestRunStatsChange(lastChunkArgs);
            }

            // Update runstats, executorUris, etc. 
            // we need this data when we send the final runcomplete 
            runDataAggregator.Aggregate(
                testRunCompleteArgs.TestRunStatistics,
                executorUris,
                testRunCompleteArgs.Error,
                testRunCompleteArgs.ElapsedTimeInRunningTests,
                testRunCompleteArgs.IsAborted,
                testRunCompleteArgs.IsCanceled,
                runContextAttachments,
                testRunCompleteArgs.AttachmentSets);

            // Do not send TestRunComplete to actual test run handler
            // We need to see if there are still sources to execute - let the parallel manager decide
            var parallelRunComplete = this.parallelProxyExecutionManager.HandlePartialRunComplete(
                    this.proxyExecutionManager,
                    testRunCompleteArgs,
                    null, // lastChunk should be null as we already sent this data above
                    runContextAttachments,
                    executorUris);

            if (parallelRunComplete)
            {
                // todo : Merge Code Coverage files here
                var completedArgs = new TestRunCompleteEventArgs(runDataAggregator.GetAggregatedRunStats(),
                    runDataAggregator.IsCanceled,
                    runDataAggregator.IsAborted,
                    runDataAggregator.GetAggregatedException(),
                    new Collection<AttachmentSet>(runDataAggregator.RunCompleteArgsAttachments),
                    runDataAggregator.ElapsedTime);

                // In case of sequential execution - RawMessage would have contained a 'TestRunCompletePayload' object
                // To send a rawmessge - we need to create rawmessage from an aggregated payload object 
                var testRunCompletePayload = new TestRunCompletePayload()
                {
                    ExecutorUris = runDataAggregator.ExecutorUris,
                    LastRunTests = null,
                    RunAttachments = runDataAggregator.RunContextAttachments,
                    TestRunCompleteArgs = completedArgs
                };

                // we have to send rawmessages as we block the runcomplete actual raw messages
                ConvertToRawMessageAndSend(MessageType.ExecutionComplete, testRunCompletePayload);

                // send actual test runcomplete to clients
                this.actualRunEventsHandler.HandleTestRunComplete(
                   completedArgs, null, runDataAggregator.RunContextAttachments, runDataAggregator.ExecutorUris);
            }
        }

        public void HandleRawMessage(string rawMessage)
        {
            // In case of parallel - we can send everything but handle complete
            // HandleComplete is not true-end of the overall execution as we only get completion of one executor here
            // Always aggregate data, deserialize and raw for complete events
            var message = this.dataSerializer.DeserializeMessage(rawMessage);

            // Do not deserialize further - just send if not execution complete
            if(!string.Equals(MessageType.ExecutionComplete, message.MessageType))
            {
                this.actualRunEventsHandler.HandleRawMessage(rawMessage);
            }
        }

        public void HandleTestRunStatsChange(TestRunChangedEventArgs testRunChangedArgs)
        {
            this.actualRunEventsHandler.HandleTestRunStatsChange(testRunChangedArgs);
        }

        public void HandleLogMessage(TestMessageLevel level, string message)
        {
            this.actualRunEventsHandler.HandleLogMessage(level, message);
        }

        public int LaunchProcessWithDebuggerAttached(TestProcessStartInfo testProcessStartInfo)
        {
            return this.actualRunEventsHandler.LaunchProcessWithDebuggerAttached(testProcessStartInfo);
        }

        private void ConvertToRawMessageAndSend(string messageType, object payload)
        {
            var rawMessage = this.dataSerializer.SerializePayload(messageType, payload);
            this.actualRunEventsHandler.HandleRawMessage(rawMessage);
        }
    }
}
