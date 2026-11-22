// Setup Context Menus on installation
chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({
    id: "nexload-download-link",
    title: "Download with NexLoad Pro",
    contexts: ["link", "video", "audio", "image"]
  });

  chrome.contextMenus.create({
    id: "nexload-grab-page-media",
    title: "Grab Media from this page with NexLoad Pro",
    contexts: ["page", "video"]
  });
});

// Handle Context Menu Clicks
chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  let targetUrl = "";

  if (info.menuItemId === "nexload-download-link") {
    targetUrl = info.linkUrl || info.srcUrl || "";
  } else if (info.menuItemId === "nexload-grab-page-media") {
    targetUrl = info.srcUrl || info.pageUrl || tab?.url || "";
  }

  if (targetUrl) {
    try {
      await fetch('http://localhost:3015/add-download', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          url: targetUrl,
          filename: ""
        })
      });
    } catch (err) {
      console.warn("Could not reach NexLoad desktop application. Ensure app is running.");
    }
  }
});

// Auto-intercept Browser Downloads
chrome.downloads.onCreated.addListener(async (downloadItem) => {
  // Ignore links to localhost or non-http protocols to avoid infinite loop
  if (!downloadItem.url ||
      downloadItem.url.startsWith('http://localhost') || 
      downloadItem.url.startsWith('http://127.0.0.1') ||
      !downloadItem.url.startsWith('http')) {
    return;
  }

  try {
    // Check if the WPF App (local server) is running
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 250); // 250ms check

    const response = await fetch('http://localhost:3015/ping', {
      method: 'GET',
      signal: controller.signal
    });

    clearTimeout(timeoutId);

    if (response.ok) {
      // Desktop app is active! Cancel browser's default download
      chrome.downloads.cancel(downloadItem.id);
      
      // Erase it from browser's list to avoid "Cancelled" shelf items
      chrome.downloads.erase({ id: downloadItem.id });

      // Send download details to Desktop App
      await fetch('http://localhost:3015/add-download', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          url: downloadItem.url,
          filename: downloadItem.filename || ""
        })
      });
    }
  } catch (error) {
    // Desktop App is not running or timed out.
    // Let the browser handle the download naturally (fallback).
    console.log("NexLoad Desktop App is not active. Fallback to native browser download.");
  }
});
