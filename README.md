![tcpTrigger Logo](https://github.com/R-Smith/tcpTrigger/raw/master/tcpTrigger.Editor/Resources/tcpTrigger%20Logo.png?raw=true)

tcpTrigger
==========

tcpTrigger is a Windows service intended to notify you of incoming network connections.  You specify a TCP port to monitor and an action to take.  Actions taken include: sending a notification email and/or launching an external application or script.  Your action will then be triggered each time an incoming connection is attempted on your specified port.

##### Editor Screenshot
![tcpTrigger Editor](https://github.com/R-Smith/supporting-docs/raw/master/tcpTrigger/tcpTrigger.png?raw=true "tcpTrigger Editor")


#### Intrusion Detection

For a simple, yet effective, internal intrusion detection system, deploy tcpTrigger just as you would a honeypot: to an unused system with no legitimate services running on it.  Configure tcpTrigger to monitor ICMP echo requests and common ports such as 80, 443, 23, 135, and/or 445.  In order to discover live hosts and running services, attackers will typically scan your network.  If your tcpTrigger system gets probed, you will be notified.  There does not need to be an actual service listening on the ports that you choose to monitor.  Detection works the same whether the port is open or closed.  It will even detect 'half-open' scans used by [Nmap](https://nmap.org/) and other port scanners.


#### Connection Notifier

Want to know when someone is connecting to your important services?  Use tcpTrigger to monitor the ports you're intersted in and you're set.  You'll get an email each time someone connects, or you can even kick off a script.


Download
--------
### [Click here to download latest .msi installer](https://github.com/R-Smith/supporting-docs/raw/master/tcpTrigger/tcpTrigger%20Setup.msi)

### [Click here to download the source](https://github.com/R-Smith/tcpTrigger/archive/master.zip)

##### Notes
* .NET 3.5 or greater is required to run the service.
* .NET 4.5 or greater is required to run the graphical configuration editor.
* The installer does not do a prerequisites check, so make sure you have the required .NET frameworks.
* The pre-compiled installer is not code-signed, so you will get a scary warning when you run it.
* My build environment is Microsoft Visual Studio Community 2015 and WiX Toolset v3.10.
