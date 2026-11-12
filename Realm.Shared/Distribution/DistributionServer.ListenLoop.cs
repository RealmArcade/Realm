using System.Net;
using System.Text;
using System.Text.Json;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

/// <summary>starting the listener and dispatching each request to its route.</summary>
public partial class DistributionServer
	{
	public void Start(int port)
	{
		_cancellationTokenSource = new CancellationTokenSource();
		_listener = new HttpListener();
		_listener.Prefixes.Add($"http://127.0.0.1:{port}/");
		_listener.Prefixes.Add($"http://localhost:{port}/");

		try
		{
			_listener.Start();
			BoundPort = port;
			_isRunning = true;
			Task.Run(() => ListenLoopAsync(_listener, _cancellationTokenSource.Token));
		}
		catch (HttpListenerException)
		{
			_listener = new HttpListener();
			_listener.Prefixes.Add($"http://127.0.0.1:{port}/");
			_listener.Start();
			BoundPort = port;
			_isRunning = true;
			Task.Run(() => ListenLoopAsync(_listener, _cancellationTokenSource.Token));
		}
	}

	public void Stop()
	{
		_isRunning = false;
		_cancellationTokenSource?.Cancel();

		try
		{
			if (_listener != null && _listener.IsListening)
			{
				_listener.Stop();
				_listener.Close();
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[DistributionServer] could not stop the HTTP listener: {ex.GetType().Name}: {ex.Message}");
		}
	}

	private async Task ListenLoopAsync(HttpListener listener, CancellationToken cancellationToken)
	{
		while (_isRunning && !cancellationToken.IsCancellationRequested)
		{
			bool continueListening = await TryProcessNextRequestAsync(listener, cancellationToken);
			if (!continueListening) break;
		}
	}

	private async Task<bool> TryProcessNextRequestAsync(HttpListener listener, CancellationToken cancellationToken)
	{
		try
		{
			var context = await listener.GetContextAsync();
			_ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
			return true;
		}
		catch
		{
			return _isRunning;
		}
	}

	private async Task HandleRequestAsync(HttpListenerContext context)
	{
		var request = context.Request;
		var response = context.Response;

		string path = request.Url?.LocalPath.TrimEnd('/') ?? string.Empty;
		string method = request.HttpMethod.ToUpperInvariant();

		try
		{
			Task? routeTask = GetRouteTask(context, path, method);
			if (routeTask != null)
			{
				await routeTask;
				return;
			}

			response.StatusCode = (int)HttpStatusCode.NotFound;
			response.Close();
		}
		catch (Exception exception)
		{
			await HandleRequestExceptionAsync(response, exception);
		}
	}

	private Task? GetRouteTask(HttpListenerContext context, string path, string method)
	{
		string lowerPath = path.ToLowerInvariant();
		return lowerPath switch
		{
			"/api/assets/bundle" when method == "POST" => HandleAssetBundleEndpointAsync(context),
			_ when lowerPath.StartsWith("/api/assets/") => HandleAssetEndpointAsync(context, method, path.Substring("/api/assets/".Length)),
			"/api/publish_map/upload_asset" => HandleAssetEndpointAsync(context, method, string.Empty),
			_ when lowerPath.StartsWith("/api/manifests") || lowerPath == "/api/admin/remove_manifest" => HandleManifestEndpointAsync(context, method, path),
			"/api/seeders/catalog" when method == "GET" => HandleCatalogEndpointAsync(context.Response),
			"/api/seeders/bloom_headers" when method == "GET" => HandleBloomHeadersEndpointAsync(context.Response),
			"/api/seeders/sync_headers" when method == "POST" => HandleHeaderSyncEndpointAsync(context.Request, context.Response),
			_ => null
		};
	}

	private async Task HandleRequestExceptionAsync(HttpListenerResponse response, Exception exception)
	{
		try
		{
			response.StatusCode = (int)HttpStatusCode.InternalServerError;
			byte[] errorBytes = Encoding.UTF8.GetBytes(exception.Message);
			await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
			response.Close();
		}
		catch
		{
			Console.Error.WriteLine($"[DistributionServer] could not write the error response: {exception.GetType().Name}: {exception.Message}");
		}
	}
}
