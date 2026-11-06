# Sends one JSON line to the engine's pipe and prints the reply line.
param(
    [Parameter(Mandatory)][string]$Line,
    [int]$TimeoutMs = 5000
)
$ErrorActionPreference = 'Stop'
$client = [System.IO.Pipes.NamedPipeClientStream]::new('.', 'sahar', [System.IO.Pipes.PipeDirection]::InOut)
try {
    $client.Connect($TimeoutMs)
    $enc = [System.Text.UTF8Encoding]::new($false)
    $writer = [System.IO.StreamWriter]::new($client, $enc)
    $writer.NewLine = "`n"
    $writer.AutoFlush = $true
    $reader = [System.IO.StreamReader]::new($client, $enc)
    $writer.WriteLine($Line)
    $reader.ReadLine()
}
finally {
    $client.Dispose()
}
