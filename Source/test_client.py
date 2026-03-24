#!/usr/bin/env python3
"""
Test client for TWX Proxy - demonstrates character-by-character transmission
This client sends data immediately without line buffering
"""

import socket
import sys
import time
import select

def test_char_by_char(host='localhost', port=2602):
    """Connect to proxy and send characters one at a time"""
    
    print(f"Connecting to {host}:{port}...")
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.connect((host, port))
    sock.setblocking(False)
    
    print("Connected!")
    print("\nTest 1: Sending $c command (should connect immediately)")
    print("Sending: $ (waiting 0.5s)")
    sock.send(b'$')
    time.sleep(0.5)
    
    print("Sending: c (should trigger connection)")
    sock.send(b'c')
    time.sleep(1)
    
    # Read any response
    try:
        response = sock.recv(4096)
        print(f"\nReceived: {response.decode('ascii', errors='replace')}")
    except:
        pass
    
    print("\nTest 2: Sending 'hello' character by character")
    for char in 'hello':
        print(f"Sending: {char}")
        sock.send(char.encode())
        time.sleep(0.3)
    
    sock.send(b'\r\n')
    time.sleep(1)
    
    # Read response
    try:
        response = sock.recv(4096)
        print(f"\nReceived: {response.decode('ascii', errors='replace')}")
    except:
        pass
    
    print("\nTest complete!")
    sock.close()

if __name__ == '__main__':
    host = sys.argv[1] if len(sys.argv) > 1 else 'localhost'
    port = int(sys.argv[2]) if len(sys.argv) > 2 else 2602
    
    try:
        test_char_by_char(host, port)
    except ConnectionRefusedError:
        print(f"ERROR: Could not connect to {host}:{port}")
        print("Make sure the proxy is running first")
        sys.exit(1)
    except KeyboardInterrupt:
        print("\nInterrupted")
        sys.exit(0)
