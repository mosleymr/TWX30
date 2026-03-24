#!/bin/bash

# Test script for TWX30 network proxy
echo "==================================="
echo "TWX30 Network Proxy Integration Test"
echo "==================================="
echo ""

# Start mock server
echo "[1/4] Starting mock game server on port 2002..."
cd /Users/mosleym/Code/twxproxy/TWX26/TWX30/Source/MockServer
nohup dotnet run --project MockServer.csproj 2002 > /tmp/mockserver.log 2>&1 &
MOCK_PID=$!
sleep 2

if ! ps -p $MOCK_PID > /dev/null; then
    echo "ERROR: Mock server failed to start"
    cat /tmp/mockserver.log
    exit 1
fi

echo "✓ Mock server running (PID: $MOCK_PID)"

# Start proxy
echo ""
echo "[2/4] Starting TWX proxy (localhost:2002 -> localhost:2602)..."
cd /Users/mosleym/Code/twxproxy/TWX26/TWX30/Source/TestNetworkDB
nohup dotnet run --project TestNetworkDB.csproj "Test Game" localhost 2002 2602 > /tmp/proxy.log 2>&1 &
PROXY_PID=$!
sleep 3

if ! ps -p $PROXY_PID > /dev/null; then
    echo "ERROR: Proxy failed to start"
    cat /tmp/proxy.log
    kill $MOCK_PID
    exit 1
fi

echo "✓ Proxy running (PID: $PROXY_PID)"

# Test communication
echo ""
echo "[3/4] Testing manual connection with \$c command..."
echo "Note: Proxy no longer auto-connects - requires \$c command from client"
echo ""

# Use a persistent connection
(
    sleep 1
    echo "Typing \$c to connect..."
    printf '$c'
    sleep 2
    echo "Test message 1"
    sleep 1
    echo "Test message 2"
    sleep 1
    echo "\$STATUS\$"
    sleep 1
) | nc localhost 2602 > /tmp/client_response.txt &
NC_PID=$!

sleep 6

# Show results
echo ""
echo "[4/4] Results:"
echo ""
echo "--- Mock Server Log ---"
tail -20 /tmp/mockserver.log
echo ""
echo "--- Proxy Log ---"
tail -30 /tmp/proxy.log
echo ""
echo "--- Client Response ---"
cat /tmp/client_response.txt

# Cleanup
echo ""
echo "Cleaning up..."
kill $MOCK_PID $PROXY_PID $NC_PID 2>/dev/null
sleep 1

echo ""
echo "==================================="
echo "Test complete!"
echo "==================================="
