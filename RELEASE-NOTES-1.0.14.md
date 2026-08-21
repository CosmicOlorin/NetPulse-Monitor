# NetPulse Monitor 1.0.14

This release corrects false Internet-outage detection.

- A failed response from the selected ICMP ping target no longer proves that the Internet connection is down.
- NetPulse confirms an outage with independent TCP connectivity probes before changing the connection state to OFFLINE or sending an outage notification.
- If normal Internet traffic remains available, the failed ping is retained as target packet loss while the connection stays ONLINE.
- Includes the CID-qualified LTE history identity changes introduced in 1.0.13.
