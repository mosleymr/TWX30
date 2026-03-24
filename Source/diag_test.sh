#!/bin/bash

# Diagnostic test to show exactly what's being transmitted
echo "========================================="
echo "TWX30 Network Diagnostic Test"
echo "========================================="
echo ""

if [ -z "$1" ]; then
    echo "Usage: $0 <server> <port> [listen-port]"
    echo "Example: $0 localhost 2002 2602"
    echo "Example: $0 twgs.com 2002 2602"
    exit 1
fi

SERVER=$1
PORT=$2
LISTEN=${3:-2602}

# Clean up
pkill -f "TestNetworkDB|MockServer" 2>/dev/null
sleep 1

echo "Target: $SERVER:$PORT"
echo "Listen: localhost:$LISTEN"
echo ""

# Start proxy with verbose output
echo "Starting proxy..."
cd /Users/mosleym/Code/twxproxy/TWX26/TWX30/Source/TestNetworkDB
dotnet run --project TestNetworkDB.csproj "Diagnostic" "$SERVER" "$PORT" "$LISTEN" > /tmp/diag_proxy.log 2>&1 &
PROXY_PID=$!
sleep 3

echo "Proxy PID: $PROXY_PID"
echo ""

# Test connection
echo "Testing connection (will send \$c command)..."
echo ""

(
    sleep 1
    printf '$c'
    sleep 3
    printf 'Hello\r\n'
    sleep 3
    printf 'quit\r\n'
    sleep 2
) | nc -v localhost $LISTEN 2>&1 | tee /tmp/diag_client.log &
NC_PID=$!

sleep 10

# Show results
echo ""
echo "========================================="
echo "DIAGNOSTIC RESULTS"
echo "========================================="
echo ""

echo "CLIENT OUTPUT:"
echo "---"
cat /tmp/diag_client.log
echo "---"
echo ""

echo "PROXY LOG (last 50 lines):"
echo "---"
tail -50 /tmp/diag_proxy.log
echo "---"
echo ""

# Cleanup
kill $PROXY_PID $NC_PID 2>/dev/null
pkill -f TestNetworkDB 2>/dev/null
sleep 1

echo "Test complete"
