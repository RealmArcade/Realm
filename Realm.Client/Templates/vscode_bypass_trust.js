(async () => {
	const DB_NAME = 'vscode-web-db';
	const STORE_NAME = 'vscode-userdata-store'; 
	const TARGET_KEY = '/User/settings.json';

	const request = indexedDB.open(DB_NAME);

	request.onsuccess = (event) => {
		const db = event.target.result;
		const transaction = db.transaction([STORE_NAME], 'readwrite');
		const store = transaction.objectStore(STORE_NAME);
		
		const getRequest = store.get(TARGET_KEY);

		getRequest.onsuccess = () => {
			let config = {};
			const rawData = getRequest.result;

			if (rawData) {
				const buffer = rawData instanceof Uint8Array ? rawData : rawData.value;
				
				if (buffer instanceof Uint8Array) {
					try {
						const decoder = new TextDecoder('utf-8');
						const jsonString = decoder.decode(buffer);
						if (jsonString.trim()) {
							config = JSON.parse(jsonString);
						}
					} catch (e) {
						console.warn("Error parsing existing binary settings. Resetting configuration layer.", e);
					}
				}
			}

			config["security.workspace.trust.enabled"] = false;
			config["security.workspace.trust.startupPrompt"] = "never";

			const updatedJsonString = JSON.stringify(config, null, '\t');
			const encoder = new TextEncoder();
			const encodedUint8Array = encoder.encode(updatedJsonString);

			let putPayload;
			if (rawData && typeof rawData === 'object' && !(rawData instanceof Uint8Array) && 'key' in rawData) {
				putPayload = { key: TARGET_KEY, value: encodedUint8Array };
			} else {
				putPayload = encodedUint8Array;
			}

			const putRequest = store.keyPath === null || !store.keyPath
				? store.put(putPayload, TARGET_KEY)
				: store.put(putPayload);

			putRequest.onsuccess = () => {
				console.log("%c[Success] Restricted mode successfully disabled via binary mutation! Reloading...", "color: #00ff00; font-weight: bold;");
				window.location.reload();
			};

			putRequest.onerror = (e) => console.error("Failed to write binary buffer to IndexedDB store:", e);
		};
	};

	request.onerror = () => console.error("Could not establish a database connection to:", DB_NAME);
})();
