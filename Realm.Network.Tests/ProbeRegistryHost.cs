using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Realm.Shared.Reachability;

namespace Realm.Network.Tests;

/// <summary>
///     Serves a registry node's two reachability routes in this process and over a real socket, so the host half of the
///     rehearsal is exercised against the same handler decisions the deployed node makes rather than against a fake of them.
/// </summary>
public class ProbeRegistryHost : IDisposable
{
	private readonly WebApplication _app;
	private readonly Task _runTask;

	public ProbeRegistryHost(UdpReachabilityProbeService probe)
	{
		int port = FreeTcpPort();

		var builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));

		_app = builder.Build();

		_app.MapPost(ReachabilityRoutes.Begin, (ReachabilityBeginRequest request, HttpContext context) =>
		{
			string sourceAddress = (context.Connection.RemoteIpAddress?.MapToIPv4() ?? IPAddress.Loopback).ToString();
			ReachabilityEndpointReply reply = ReachabilityEndpointHandlers.Begin(probe, request, sourceAddress, context.Connection.RemotePort);
			return Results.Json(reply.Body, statusCode: reply.StatusCode);
		});

		_app.MapGet($"{ReachabilityRoutes.Report}/{{probeId}}", (string probeId) =>
		{
			ReachabilityEndpointReply reply = ReachabilityEndpointHandlers.Read(probe, probeId);
			return Results.Json(reply.Body, statusCode: reply.StatusCode);
		});

		BaseUrl = $"http://127.0.0.1:{port}";
		_runTask = _app.StartAsync();
		_runTask.GetAwaiter().GetResult();
	}

	public string BaseUrl { get; }

	public void Dispose()
	{
		try
		{
			_app.StopAsync().GetAwaiter().GetResult();
		}
		catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
		{
		}

		_app.DisposeAsync().AsTask().GetAwaiter().GetResult();
	}

	private static int FreeTcpPort()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}
}

/// <summary>Ports that nothing is listening on, for the cases where a port is exactly what is missing.</summary>
public static class FreePort
{
	public static int Udp()
	{
		using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
	}

	public static int Tcp()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}
}

/// <summary>Polls a probe until it stops waiting, which is how a host learns the answer its registry node measured.</summary>
public static class ProbePolling
{
	public static async Task<ReachabilityReport> WaitForResultAsync(UdpReachabilityProbeService probes, ReachabilityTicket ticket, TimeSpan budget)
	{
		DateTime deadline = DateTime.UtcNow + budget;
		ReachabilityReport? report = null;

		while (DateTime.UtcNow < deadline)
		{
			report = probes.Read(ticket.ProbeId);
			if (report == null) break;
			if (report.State != ReachabilityStates.Waiting) return report;
			await Task.Delay(100);
		}

		return report ?? new ReachabilityReport(ticket.ProbeId, ReachabilityStates.Silent, false, string.Empty, 0, string.Empty, 0, string.Empty, 0);
	}
}
