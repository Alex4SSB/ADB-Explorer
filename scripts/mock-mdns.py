"""Mock mDNS responder that advertises ADB services, for testing. See --help."""
import argparse
import json
import os
import random
import socket
import struct
import threading
import time

USAGE = """examples:
  mock-mdns.py                                   _adb-tls-connect, port 37000
  mock-mdns.py --type _adb-tls-pairing._tcp --port 37001
  mock-mdns.py --control services.json           services read from a file

The control file holds a JSON list such as
  [{"type": "_adb-tls-pairing._tcp", "name": "adb-ABC-xyz", "port": 37001}]
It is re-read when it changes: new services are announced, removed ones get a goodbye.
An entry with "target": "127.0.0.1:47001" also relays its port there, for this PC only.
"""

MDNS_ADDR, MDNS_PORT = "224.0.0.251", 5353
TYPE_A, TYPE_PTR, TYPE_TXT, TYPE_SRV, TYPE_ANY = 1, 12, 16, 33, 255
CLASS_IN, CACHE_FLUSH = 1, 0x8000


def enc_name(name):
    out = b""
    for label in name.rstrip(".").split("."):
        b = label.encode()
        out += bytes([len(b)]) + b
    return out + b"\0"


def dec_name(buf, off):
    labels, jumped, end = [], False, off
    while True:
        n = buf[off]
        if n & 0xC0 == 0xC0:
            if not jumped:
                end = off + 2
            off = ((n & 0x3F) << 8) | buf[off + 1]
            jumped = True
            continue
        if n == 0:
            if not jumped:
                end = off + 1
            return ".".join(labels).lower(), end
        labels.append(buf[off + 1:off + 1 + n].decode(errors="replace"))
        off += 1 + n


def rr(name, rtype, rdata, ttl=120, flush=False):
    cls = CLASS_IN | (CACHE_FLUSH if flush else 0)
    return enc_name(name) + struct.pack("!HHIH", rtype, cls, ttl, len(rdata)) + rdata


def pipe(src, dst):
    try:
        while data := src.recv(65536):
            dst.sendall(data)
    except OSError:
        pass
    finally:
        for s in (src, dst):
            try:
                s.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass


class Relay:
    """Forwards connections from this PC to target (host:port); other hosts are refused."""

    def __init__(self, ip, port, target):
        host, tport = target.rsplit(":", 1)
        self.target = (host, int(tport))
        self.server = socket.socket()
        self.allowed = {ip, "127.0.0.1"}
        # On Windows SO_REUSEADDR would let two relays share a port
        if hasattr(socket, "SO_EXCLUSIVEADDRUSE"):
            self.server.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        self.server.bind((ip, port))
        self.server.listen(8)
        threading.Thread(target=self.serve, daemon=True).start()

    def serve(self):
        while True:
            try:
                client, addr = self.server.accept()
            except OSError:
                return
            if addr[0] not in self.allowed:
                print(f"relay refused {addr[0]}: not this PC", flush=True)
                client.close()
                continue
            try:
                upstream = socket.create_connection(self.target, timeout=5)
                upstream.settimeout(None)
            except OSError as e:
                print(f"relay to {self.target} failed: {e}", flush=True)
                client.close()
                continue
            print(f"relay {addr[0]}:{addr[1]} -> {self.target[0]}:{self.target[1]}", flush=True)
            threading.Thread(target=pipe, args=(client, upstream), daemon=True).start()
            threading.Thread(target=pipe, args=(upstream, client), daemon=True).start()

    def close(self):
        self.server.close()


class Service:
    def __init__(self, stype, name, ip, port, target=None):
        self.type = stype.rstrip(".") + ".local"
        self.instance = f"{name}.{self.type}"
        self.host = f"{name}.local"
        self.ip, self.port = ip, int(port)
        self.target = target
        self.relay = None

    @property
    def key(self):
        return (self.instance.lower(), self.ip, self.port, self.target)

    def records(self, ttl=120):
        txt = bytes([30]) + b"v=ADB_SECURE_SERVICE_VERSION=1"
        return {
            "ptr": rr(self.type, TYPE_PTR, enc_name(self.instance), ttl),
            "srv": rr(self.instance, TYPE_SRV, struct.pack("!HHH", 0, 0, self.port) + enc_name(self.host), ttl, True),
            "txt": rr(self.instance, TYPE_TXT, txt, ttl, True),
            "a": rr(self.host, TYPE_A, socket.inet_aton(self.ip), ttl, True),
        }

    def packet(self, keys, ttl=120):
        recs = self.records(ttl)
        answers = [recs[k] for k in keys]
        extra = [recs[k] for k in recs if k not in keys]
        hdr = struct.pack("!HHHHHH", 0, 0x8400, 0, len(answers), 0, len(extra))
        return hdr + b"".join(answers) + b"".join(extra)

    def match(self, qname, qtype):
        if qname == self.type.lower() and qtype in (TYPE_PTR, TYPE_ANY):
            return ["ptr"]
        if qname == self.instance.lower() and qtype in (TYPE_SRV, TYPE_TXT, TYPE_ANY):
            return ["srv", "txt"]
        if qname == self.host.lower() and qtype in (TYPE_A, TYPE_ANY):
            return ["a"]
        return None


def lan_ip():
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        s.connect(("10.255.255.255", 1))
        return s.getsockname()[0]
    except OSError:
        return "127.0.0.1"
    finally:
        s.close()


def load_control(path, ip):
    try:
        with open(path, encoding="utf-8-sig") as f:
            items = json.load(f)
    except (OSError, ValueError):
        return None
    return [Service(i["type"], i["name"], i.get("ip", ip), i["port"], i.get("target")) for i in items]


def main():
    p = argparse.ArgumentParser(epilog=USAGE, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--type", default="_adb-tls-connect._tcp")
    p.add_argument("--name", default=f"adb-MOCK{random.randint(1000, 9999)}-AbCdEf")
    p.add_argument("--ip", default=lan_ip(), help="IP in the A record (default: LAN IP)")
    p.add_argument("--port", type=int, default=37000)
    p.add_argument("--iface", default=None, help="interface IP to send/join on (default: --ip)")
    p.add_argument("--control", default=None, help="JSON file listing the services; re-read on change")
    p.add_argument("-v", "--verbose", action="store_true")
    a = p.parse_args()
    iface = a.iface or (a.ip if a.ip != "127.0.0.1" else "0.0.0.0")

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM, socket.IPPROTO_UDP)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind(("", MDNS_PORT))
    sock.setsockopt(socket.IPPROTO_IP, socket.IP_ADD_MEMBERSHIP,
                    socket.inet_aton(MDNS_ADDR) + socket.inet_aton(iface))
    sock.setsockopt(socket.IPPROTO_IP, socket.IP_MULTICAST_IF, socket.inet_aton(iface))
    sock.setsockopt(socket.IPPROTO_IP, socket.IP_MULTICAST_LOOP, 1)
    sock.setsockopt(socket.IPPROTO_IP, socket.IP_MULTICAST_TTL, 255)
    sock.settimeout(0.25)
    dest = (MDNS_ADDR, MDNS_PORT)

    services, control_mtime = [], None
    if not a.control:
        services = [Service(a.type, a.name, a.ip, a.port)]

    # announce schedule per service key: [next time, interval]
    schedule = {}

    def set_services(new):
        nonlocal services
        new_keys = {s.key for s in new}
        kept = {s.key: s for s in services if s.key in new_keys}
        for s in services:
            if s.key not in new_keys:
                sock.sendto(s.packet(["ptr", "srv", "txt", "a"], ttl=0), dest)
                schedule.pop(s.key, None)
                if s.relay:
                    s.relay.close()
                print(f"removed {s.instance} {s.ip}:{s.port}", flush=True)
        result = []
        for s in new:
            if s.key in kept:
                result.append(kept[s.key])
                continue
            if s.target:
                try:
                    s.relay = Relay(s.ip, s.port, s.target)
                except OSError as e:
                    print(f"relay on port {s.port} failed: {e}", flush=True)
            schedule[s.key] = [0.0, 1.0]
            result.append(s)
            suffix = f" (relayed to {s.target})" if s.target else ""
            print(f"advertising {s.instance} -> {s.ip}:{s.port}{suffix}", flush=True)
        services = result

    set_services(services)
    print(f"ready on {iface}. Ctrl+C stops (sends goodbye).", flush=True)
    try:
        while True:
            if a.control:
                try:
                    mtime = os.stat(a.control).st_mtime_ns
                except OSError:
                    mtime = None
                if mtime != control_mtime:
                    loaded = load_control(a.control, a.ip) if mtime else []
                    if loaded is not None:
                        control_mtime = mtime
                        set_services(loaded)

            now = time.time()
            for s in services:
                entry = schedule[s.key]
                if now >= entry[0]:
                    sock.sendto(s.packet(["ptr"]), dest)
                    entry[0], entry[1] = now + entry[1], min(entry[1] * 2, 30)

            try:
                data, src = sock.recvfrom(9000)
            except (socket.timeout, ConnectionResetError):
                continue
            if len(data) < 12:
                continue
            _, flags, qd = struct.unpack("!HHH", data[:6])
            if flags & 0x8000:
                continue
            off, wanted = 12, []
            try:
                for _ in range(qd):
                    qname, off = dec_name(data, off)
                    qtype, _ = struct.unpack("!HH", data[off:off + 4])
                    off += 4
                    if a.verbose:
                        print(f"  query from {src[0]}:{src[1]} {qname} type={qtype}", flush=True)
                    if qname == "_services._dns-sd._udp.local" and qtype in (TYPE_PTR, TYPE_ANY):
                        for t in {s.type for s in services}:
                            hdr = struct.pack("!HHHHHH", 0, 0x8400, 0, 1, 0, 0)
                            sock.sendto(hdr + rr("_services._dns-sd._udp.local", TYPE_PTR, enc_name(t)), dest)
                    for s in services:
                        m = s.match(qname, qtype)
                        if m:
                            wanted.append((s, m))
            except (IndexError, struct.error):
                continue
            for s, keys in wanted:
                pkt = s.packet(keys)
                sock.sendto(pkt, dest)
                if src[1] != MDNS_PORT:  # legacy unicast query
                    sock.sendto(data[:2] + pkt[2:], src)
                if a.verbose:
                    print(f"answered {keys} for {s.instance}", flush=True)
    except KeyboardInterrupt:
        for s in services:
            sock.sendto(s.packet(["ptr", "srv", "txt", "a"], ttl=0), dest)
        print("goodbye sent", flush=True)


if __name__ == "__main__":
    main()
