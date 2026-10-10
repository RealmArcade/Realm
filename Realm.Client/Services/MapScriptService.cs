using System;
using System.Runtime.Loader;
using Realm.MapAPI;

namespace Realm.Client.Services;

public class MapScriptService
{
	public IMapScript? ActiveMapScript { get; set; }
	public string? PendingMapScriptPath { get; set; }
	public AssemblyLoadContext? MapScriptLoadContext { get; set; }
    
    public void ClearAll()
    {
        ActiveMapScript = null;
        PendingMapScriptPath = null;
        if (MapScriptLoadContext != null)
        {
            MapScriptLoadContext.Unload();
            MapScriptLoadContext = null;
        }
    }
}
