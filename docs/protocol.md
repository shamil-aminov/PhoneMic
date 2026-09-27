# PhoneMic wire protocol, version 1

The phone is the client, the PC is the server. One UDP socket and one TCP
listener share the same port on the PC (default `50505`).

* **Wi-Fi** uses UDP. Every datagram is exactly one packet.
* **USB** uses TCP through `adb reverse tcp:50505 tcp:50505`, so the phone dials
  `127.0.0.1`. Each packet is prefixed with its length as `u16` little-endian.

All integers are little-endian. Strings are `u8 length` followed by that many
UTF-8 bytes.

## Pairing QR code

```
phonemic://pair?a=192.168.1.10,10.0.0.4&p=50505&t=K7F2QX9M&n=MY-PC
```

| Key | Meaning |
|-----|---------|
| `a` | Every IPv4 address of the PC, comma separated. The phone tries them all. |
| `p` | Port. |
| `t` | Pairing token. The PC ignores anyone who does not know it. |
| `n` | PC name, for display only. |

The phone remembers the pairing, so the QR code is scanned once. If the PC's
address changes, the phone still finds it: the HELLO is also sent to the
broadcast address, and the PC answers from its current address.

## Packet header (8 bytes)

| Offset | Type | Field |
|--------|------|-------|
| 0 | `u8[2]` | Magic `'P' 'M'` |
| 2 | `u8` | Protocol version, `1` |
| 3 | `u8` | Packet type |
| 4 | `u32` | Session id, chosen at random by the phone for each connection |

## Packet types

### 1 `HELLO` phone → PC

| Type | Field |
|------|-------|
| str | Pairing token |
| str | Device id (random, stable across app launches) |
| str | Device name, e.g. `SOG02` |
| `u32` | Sample rate, Hz |
| `u8` | Channels (always 1) |

Sent once a second until a `WELCOME` arrives.

### 2 `WELCOME` PC → phone

| Type | Field |
|------|-------|
| `u8` | Status: `0` ok, `1` wrong token, `2` busy with another phone |
| str | PC name |

A PC that is streaming from one phone answers another phone with `busy` until
the first one has been silent for 3 seconds. The same device id reconnecting
always takes over its old session.

### 3 `AUDIO` phone → PC

| Type | Field |
|------|-------|
| `u32` | Sequence number, starts at 0 for each session |
| `i16[]` | PCM samples, mono, until the end of the packet |

One packet carries 10 ms of audio (480 samples at 48 kHz).

### 4 `PING` phone → PC, 5 `PONG` PC → phone

| Type | Field |
|------|-------|
| `u64` | Phone timestamp in ms, echoed back unchanged in `PONG` |

The phone pings every 500 ms. Either side treats 3 seconds without any packet
from the other as a lost connection. The phone then goes back to sending
`HELLO` until it gets through again.

### 6 `BYE` either direction

No payload. Ends the session immediately.
