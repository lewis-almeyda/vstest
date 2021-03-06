// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.DataCollection
{
    using System;
    using Microsoft.VisualStudio.TestPlatform.ObjectModel;
    using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.Interfaces;
    using System.Collections.ObjectModel;
    using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
    using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.ObjectModel;
    using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.DataCollection.Interfaces;
    using Microsoft.VisualStudio.TestPlatform.Common.DataCollection;

    /// <summary>
    /// Utility class that facilitates the IPC comunication. Acts as server.
    /// </summary>
    public sealed class DataCollectionRequestSender : IDataCollectionRequestSender, IDisposable
    {
        private ICommunicationManager communicationManager;

        private IDataSerializer dataSerializer;

        /// <summary>
        /// Creates new instance of DataCollectionRequestSender.
        /// </summary>
        public DataCollectionRequestSender() : this(new SocketCommunicationManager(), JsonDataSerializer.Instance)
        {
        }

        /// <summary>
        /// Creates new instance of DataCollectionRequestSender.
        /// </summary>
        /// <param name="communicationManager"></param>
        /// <param name="dataSerializer"></param>
        internal DataCollectionRequestSender(ICommunicationManager communicationManager, IDataSerializer dataSerializer)
        {
            this.communicationManager = communicationManager;
            this.dataSerializer = dataSerializer;
        }

        /// <summary>
        /// Creates an endpoint and listens for client connection asynchronously
        /// </summary>
        /// <returns></returns>
        public int InitializeCommunication()
        {
            var port = this.communicationManager.HostServer();
            this.communicationManager.AcceptClientAsync();
            return port;
        }

        /// <summary>
        /// Waits for Request Handler to be connected 
        /// </summary>
        /// <param name="clientConnectionTimeout">Time to wait for connection</param>
        /// <returns>True, if Handler is connected</returns>
        public bool WaitForRequestHandlerConnection(int clientConnectionTimeout)
        {
            return this.communicationManager.WaitForClientConnection(clientConnectionTimeout);
        }

        /// <summary>
        /// The dispose.
        /// </summary>
        public void Dispose()
        {
            this.communicationManager?.StopServer();
        }

        /// <summary>
        /// Closes the connection
        /// </summary>
        public void Close()
        {
            this.Dispose();
            EqtTrace.Info("Closing the connection");
        }

        /// <summary>
        /// Sends the BeforeTestRunStart event and waits for result
        /// </summary>
        /// <returns>BeforeTestRunStartResult containing environment variables</returns>
        public BeforeTestRunStartResult SendBeforeTestRunStartAndGetResult(string settingsXml)
        {
            this.communicationManager.SendMessage(MessageType.BeforeTestRunStart, settingsXml);
            var message = this.communicationManager.ReceiveMessage();
            if (message.MessageType == MessageType.BeforeTestRunStartResult)
            {
                return dataSerializer.DeserializePayload<BeforeTestRunStartResult>(message);
            }

            return null;
        }

        /// <summary>
        /// Sends the AfterTestRunStart event and waits for result
        /// </summary>
        /// <returns>DataCollector attachments</returns>
        public Collection<AttachmentSet> SendAfterTestRunStartAndGetResult()
        {
            this.communicationManager.SendMessage(MessageType.BeforeTestRunStart);
            var message = this.communicationManager.ReceiveMessage();
            if (message.MessageType == MessageType.BeforeTestRunStartResult)
            {
                return dataSerializer.DeserializePayload<Collection<AttachmentSet>>(message);
            }

            return null;
        }
    }
}