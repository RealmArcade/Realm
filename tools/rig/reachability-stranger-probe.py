"""Asks a game port the question a stranger's client asks first, from an address the host has never sent toward.

Prints whether the port answered, which is what separates a forwarded port from one that only a punch opens. The datagram
is a bare ENet connect header: a game port that has not been punched never sees one, and one that is forwarded answers it.
"""

import socket
import sys

address = sys.argv[1] if len(sys.argv) > 1 else "203.0.113.2"
port = int(sys.argv[2]) if len(sys.argv) > 2 else 8999

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.bind(("203.0.113.1", 0))
sock.settimeout(2.0)
sock.sendto(b"\x08\x00" + bytes(46), (address, port))
print(f"  sent from {sock.getsockname()} to {address}:{port}")
try:
    data, remote = sock.recvfrom(2048)
    print(f"  the port answered from {remote} with {len(data)} bytes")
except socket.timeout:
    print("  no answer: a stranger's first datagram does not reach this game port")
