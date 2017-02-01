using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using BestHTTP.Decompression.Zlib;
using System.IO;
using System.Linq;

using UberLogger;
using System.IO;

/// <summary>
/// A basic file logger backend
/// </summary>
public class UberLoggerGraylog : UberLogger.ILogger
{
    private bool IncludeCallStacks;
	
	public string host;
	private Socket socket;
	private IPEndPoint endpoint;
	public int max_packet_size = 0;
	public string last_log;
	public bool is_client;
	public string sentry_url;
	public string deploy_to;
	
	public string container;
	public string service;
	public string stack_name;
	public string environment;
	public string hostname;
	
	public Dictionary<string, object> meta;
	

    /// <summary>
    /// Constructor. Make sure to add it to UberLogger via Logger.AddLogger();
    /// filename is relative to Application.persistentDataPath
    /// if includeCallStacks is true it will dump out the full callstack for all logs, at the expense of big log files.
    /// </summary>
    public UberLoggerGraylog(string host, Dictionary<string, object> m)
    {
		meta = m;
		Debug.LogWarning(string.Format("Pointing logs to: {0}", host));
		socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
		
		IPAddress ipAddress;
	    // try
        // {
        //    ipAddress = IPAddress.Parse(host);
        // }
        // catch (Exception ex)
        // {
		// 	Debug.LogWarning("Could not enable logger.  Disabling...");
		// 	enabled = false;
		// 	throw;
        // }	
		
		if (!IPAddress.TryParse (host, out ipAddress))
			ipAddress = Dns.GetHostEntry(host).AddressList[0];
			
		endpoint = new IPEndPoint(ipAddress, 12202);
		Debug.LogWarning(string.Format("Log final destination: {0}", ipAddress));
    }

    public void Log(LogInfo logInfo)
    {
        lock(this)
        {
			Dictionary<string, object> msg = new Dictionary<string ,object>(meta);
	
			msg["short_message"] = logInfo.Message;
			
			string stack = "";
			if(logInfo.Callstack.Count>0)
            {
                foreach(var frame in logInfo.Callstack)
                {
                    stack = string.Format("{0}\n{1}", stack, frame.GetFormattedMethodName());
                }
            }
			
			msg["full_message"] = stack;
			
			if (logInfo.Severity == LogSeverity.Error)
			{
				msg["level"] = 4;
				msg["level_name"] = "ERROR";
			}
			else if (logInfo.Severity == LogSeverity.Warning)
			{
				msg["level"] = 5;
				msg["level_name"] = "WARNING";
			}
			else
			{
				msg["level"] = 7;
				msg["level_name"] = "DEBUG";
			}
			
			if (logInfo.meta != null)
				msg = msg.Concat(logInfo.meta).ToDictionary(x=>x.Key, x=>x.Value);
				
			msg["channel"] = logInfo.Channel;
			
			string json = Utility.JsonSerialize(msg);
			
			byte[] send_buffer = UberLoggerGraylog.Zip(json);
			
			max_packet_size = Mathf.Max(max_packet_size, send_buffer.Length);
			
			socket.SendTo(send_buffer, endpoint);
			
			last_log = json;		
        }
	}
		
	public static byte[] Zip(string str) {
	    // var bytes = Encoding.UTF8.GetBytes(str);
		var bytes = Encoding.ASCII.GetBytes(str);
	
	    using (var msi = new MemoryStream(bytes))
	    using (var mso = new MemoryStream()) {
	        using (var gs = new GZipStream(mso, CompressionMode.Compress)) {
	            //msi.CopyTo(gs);
	            Utility.CopyTo(msi, gs);
	        }
	
	        return mso.ToArray();
	    }
	}
		
	
	public void OnApplicationPause(bool paused) 
	{
		Debug.LogWarning(string.Format("Application pause: {0}", paused));
		
		// if application came back, re-establish socket
		if (!paused)
		{
			if (socket != null)
			{
				try
				{
					socket.Close();
				}
				catch
				{}
			}
				
			socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
		}
    }
	
	public void OnDestroy()
	{
		Debug.LogWarning(string.Format("UDPLogger destroyed, removing socket"));
		if (socket != null)
		{
			try
			{
				socket.Close();
				socket = null;
			}
			catch
			{}
		}
	}
}
