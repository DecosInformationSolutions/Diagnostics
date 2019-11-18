﻿using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Xml.Serialization;
using System.Xml;

namespace Decos.Diagnostics.Trace
{
    /// <summary>
    /// Represents a trace listener that sends logging output to a remote server
    /// via UDP using the log4net XML layout.
    /// </summary>
    public class UdpTraceListener : TraceListenerBase
    {
        /// <summary>
        /// The maximum size in bytes of a datagram before messages will be 
        /// truncated. 
        /// </summary>
        public const int MaximumDatagramSize = 1472;

        private const string _defaultSource = "Trace";
        private UdpClient _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="UdpTraceListener"/> class using 
        /// the specified server address.
        /// </summary>
        /// <param name="initializeData">A string in "hostname:port" format.</param>
        public UdpTraceListener(string initializeData)
        {
            int iSeparator = initializeData.LastIndexOf(':');
            if (iSeparator < 0)
                throw new ArgumentException(initializeData + " is not in the expected format \"hostname:port\"", "initializeData");

            string hostname = initializeData.Substring(0, iSeparator);
            int port = int.Parse(initializeData.Substring(iSeparator + 1));
            _client = new UdpClient(hostname, port);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UdpTraceListener"/> class using
        /// the specified hostname and port number.
        /// </summary>
        /// <param name="hostname">The name or IP address of the remote host.</param>
        /// <param name="port">The UDP port number to use.</param>
        public UdpTraceListener(string hostname, int port)
        {
            _client = new UdpClient(hostname, port);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UdpTraceListener"/> class using 
        /// the specified server address and name.
        /// </summary>
        /// <param name="initializeData">A string in "hostname:port" format.</param>
        /// <param name="name">The name of the trace listener.</param>
        public UdpTraceListener(string initializeData, string name) : base(name)
        {
            int iSeparator = initializeData.LastIndexOf(':');
            if (iSeparator < 0)
                throw new ArgumentException(initializeData + " is not in the expected format \"hostname:port\"", "initializeData");

            string hostname = initializeData.Substring(0, iSeparator);
            int port = int.Parse(initializeData.Substring(iSeparator + 1));
            _client = new UdpClient(hostname, port);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UdpTraceListener"/> class using
        /// the specified hostname, port number and name.
        /// </summary>
        /// <param name="hostname">The name or IP address of the remote host.</param>
        /// <param name="port">The UDP port number to use.</param>
        /// /// <param name="name">The name of the trace listener.</param>
        public UdpTraceListener(string hostname, int port, string name) : base(name)
        {
            _client = new UdpClient(hostname, port);
        }

        /// <summary>
        /// Gets a value indicating whether the trace listener is thread safe.
        /// </summary>
        public override bool IsThreadSafe => true;

        /// <summary>
        /// Sends trace information and a message to remote host.
        /// </summary>
        /// <param name="e">
        /// A <see cref="TraceListenerBase.TraceEventData"/> object that contains
        /// information about the trace event.
        /// </param>
        /// <param name="message">The message to write.</param>
        protected override void TraceInternal(TraceEventData e, string message)
        {
            Send(new LogForUdp(ConstructLogMessageToWrite(e, message), GetTraceEventDataTypeWithInformationAsDefault(e)));
        }

        /// <summary>
        /// Sends trace information and an object to remote host.
        /// </summary>
        /// <param name="e">
        /// A <see cref="TraceListenerBase.TraceEventData"/> object that contains
        /// information about the trace event.
        /// </param>
        /// <param name="data">The object to write.</param>
        protected override void TraceInternal(TraceEventData e, object data)
        {
            TraceInternal(e, data.ToString());
        }

        /// <summary>
        /// Sends the specified string to the remote host.
        /// </summary>
        /// <param name="data">The string to send.</param>
        private void Send(LogForUdp data)
        {
            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(data.Serialize());
                if (buffer.Length > MaximumDatagramSize)
                {
                    System.Diagnostics.Trace.WriteLine("Datagram exceeds maximum size (" + buffer.Length + ", max: " + MaximumDatagramSize + ")");
                }
                _client.Send(buffer, buffer.Length);
            }
            catch (SocketException ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
            }
        }

        private string ConstructLogMessageToWrite(TraceEventData e, string message)
        {
            string newMessage = "[" + e.Cache.DateTime + "] ";
            newMessage += "[" + GetTraceEventDataTypeWithInformationAsDefault(e) + "] ";
            newMessage += message;
            newMessage += " [" + e.Source + "]";
            return newMessage;
        }

        private string GetTraceEventDataTypeWithInformationAsDefault(TraceEventData e)
        {
            if (string.IsNullOrEmpty(e.Type.ToString()))
                return "Information";
            else
                return e.Type.ToString();
        }
    }


    public class LogForUdp
    {
        private static readonly XmlParserContext parserContext = CreateXmlParserContext();
        private static readonly XmlSerializer serializer = new XmlSerializer(typeof(LogForUdp));

        public LogForUdp()
        {
            this.Message = "";
            this.Level = "";
        }

        public LogForUdp(string message, string level)
        {
            this.Message = message;
            this.Level = level;
        }

        public string Message { get; set; }

        public string Level { get; set; }

        public static bool CanDeserialize(string xmlFragment)
        {
            using (var reader = new XmlTextReader(xmlFragment, XmlNodeType.Element, parserContext))
            {
                return serializer.CanDeserialize(reader);
            }
        }

        public static LogForUdp Deserialize(string xmlFragment)
        {
            using (var reader = new XmlTextReader(xmlFragment, XmlNodeType.Element, parserContext))
            {
                return (LogForUdp)serializer.Deserialize(reader);
            }
        }

        public string Serialize()
        {
            var valueBuilder = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                Encoding = Encoding.UTF8,
                NewLineHandling = NewLineHandling.None,
                CheckCharacters = false
            };

            var ns = new XmlSerializerNamespaces();
            using (var writer = XmlWriter.Create(valueBuilder, settings))
            {
                serializer.Serialize(writer, this, ns);
            }

            return valueBuilder.ToString();
        }

        private static XmlParserContext CreateXmlParserContext()
        {
            var nameTable = new NameTable();
            var namespaceManager = new XmlNamespaceManager(nameTable);
            return new XmlParserContext(nameTable, namespaceManager, "en", XmlSpace.None, Encoding.UTF8);
        }
    }
}
