#!/bin/bash

# Demonstration of $c command - manual connection
echo "========================================="
echo "TWX30 Manual Connection Demo ($c command)"
echo "========================================="
echo ""

# Clean up any existing processes
pkill -f "TestNetworkDB|MockServer" 2>/dev/null
sleep 1

# Start mock server
echo "[1/3] Starting mock game server on port 2002..."
cd /Users/mosleym/Code/twxproxy/TWX26/TWX30/Source/MockServer
nohup dotnet run --project MockServer.csproj 2002 > /tmp/demo_server.log 2>&1 &
SERVER_PID=$!
sleep 2
echo "✓ Mock server running (PID: $SERVER_PID)"
echo ""

# Start proxy
echo "[2/3] Starting TWX proxy (NO auto-connect)..."
cd /Users/mosleym/Code/twxproxy/TWX26/TWX30/Source/TestNetworkDB  
nohup dotnet run --project TestNetworkDB.csproj "Demo Game" localhost 2002 2602 > /tmp/demo_proxy.log 2>&1 &
PROXY_PID=$!
sleep 3
echo "✓ Proxy running (PID: $PROXY_PID)"
echo ""

# Show initial state (no connection)
echo "[3/3] Initial State (before \$c command):"
echo "---"
grep -E "Status: Connected|Type .c to connect" /tmp/demo_proxy.log | head -2
echo "---"
echo ""

# Interactive test
echo "Now testing manual connection:"
echo "1. Sending 'hello' WITHOUT server connection"
echo "2. Typing '\$c' to connect to server"  
echo "3. Sending 'world' WITH server connection"
echo ""

(
    printf 'hello\n'
    sleep 1
    printf '$c'
    sleep 2
    printf 'world\n'
    sleep 1
) | nc localhost 2602 > /tmp/demo_client.txt 2>&1

echo "Results:"
echo "========"
echo ""
echo "CLIENT RECEIVED:"
echo "---"
cat /tmp/demo_client.txt
echo "---"
echo ""

echo "PROXY LOG (connection sequence):"
echo "---"
grep -E "Server not connected|Immediate command: c|Connected to localhost|-> Forwarded" /tmp/demo_proxy.log | tail -10
echo "---"
echo ""

echo "MOCK SERVER LOG:"
echo "---"
tail -6 /tmp/demo_server.log
echo "---"
echo ""

# Cleanup
kill $SERVER_PID $PROXY_PID 2>/dev/null
sleep 1

echo "========================================="
echo "Demo complete!"
echo "========================================="
echo ""
echo "Summary:"
echo "  • Proxy starts WITHOUT server connection"
echo "  • 'hello' discarded (no server connected)"
echo "  • \$c command connects to server immediately"
echo "  • 'world' forwarded to server and echoed back"
