# Evaluates JavaScript in the running demo app through the WebView2 DevTools protocol (QA only).
# Start the app with: $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS='--remote-debugging-port=9333'
param([Parameter(Mandatory)][string]$Js, [int]$Port = 9333)
$targets = Invoke-RestMethod "http://127.0.0.1:$Port/json"
$t = $targets | Where-Object { $_.url -like 'https://app.local/*' } | Select-Object -First 1
if (-not $t) { throw "app page not found" }
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ws.ConnectAsync([Uri]$t.webSocketDebuggerUrl, [Threading.CancellationToken]::None).Wait()
$msg = @{ id = 1; method = 'Runtime.evaluate'; params = @{ expression = $Js; awaitPromise = $true; returnByValue = $true } } | ConvertTo-Json -Depth 5 -Compress
$bytes = [Text.Encoding]::UTF8.GetBytes($msg)
$ws.SendAsync([ArraySegment[byte]]$bytes, 'Text', $true, [Threading.CancellationToken]::None).Wait()
$buf = New-Object byte[] 1048576
$sb = New-Object Text.StringBuilder
do {
    $r = $ws.ReceiveAsync([ArraySegment[byte]]$buf, [Threading.CancellationToken]::None).Result
    [void]$sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r.Count))
} while (-not $r.EndOfMessage)
$ws.Dispose()
$res = $sb.ToString() | ConvertFrom-Json
if ($res.result.exceptionDetails) { "EXCEPTION: " + ($res.result.exceptionDetails | ConvertTo-Json -Depth 6 -Compress) }
else { $res.result.result.value }
