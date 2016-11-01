![tcpTrigger Logo](https://github.com/R-Smith/tcpTrigger/raw/master/tcpTrigger.Editor/Resources/tcpTrigger%20Logo.png?raw=true)

tcpTrigger
==========

tcpTrigger is a Windows service intended to notify you of incoming network connections.  You specify a TCP port to monitor and an action to take.  Actions taken include: sending a notification email and/or launching an external application or script.  Your action will then be triggered each time an incoming connection is attempted on your specified port.

##### Editor Screenshot
![tcpTrigger Editor](https://github.com/R-Smith/supporting-docs/raw/master/tcpTrigger/tcpTrigger_1.1.png?raw=true "tcpTrigger Editor")


#### Intrusion Detection

For a simple, yet effective, internal intrusion detection system, deploy tcpTrigger.  Attackers who are unfamiliar with your network, must first map out live hosts and running services.  This is typically accomplished with a port scanner, such as [Nmap](https://nmap.org).  An intruder cannot steal sensitive documents without first discovering your file servers, and they cannot dump your user mailboxes without first discovering your email servers.  If your tcpTrigger system gets probed, you will be alerted of the intrusion.  Your tcpTrigger system can alert on incoming ICMP echo requests or on connections to any TCP port.  You can monitor ports with existing services or even ports with nothing listening.  Detection works the same whether the port is open or closed.  It will even detect 'half-open' connections used by most port scanners.  For an IDS deployment in an enterprise environment, install tcpTrigger on a dedicated system and configure it to monitor incoming ping requests and common TCP ports such as 80 and 23.  tcpTrigger automatically listens on all available IPv4 network interfaces, so you can easily monitor multiple subnets from a single installation.


#### NetBIOS Name Poison Detection

As far as I know, tcpTrigger is currently the only solution capable of detecting NetBIOS name poisoning.  The way it works is very simple:  every few minutes it sends out NetBIOS name queries for fictitious names, and if a response is returned, an alert is triggered.


Name resolution poisoning is a highly effective attack that is carried out on a local network.  Check [here](https://www.sternsecurity.com/blog/local-network-attacks-llmnr-and-nbt-ns-poisoning) for an overview.  There are two broadcast-based name resolution technologies enabled by default in Windows: LLMNR and NetBIOS over TCP/IP.  A Windows workstation will typically resolve names using DNS, but LLMNR and NBNS will be used in certain cases where DNS resolution fails.  The problem with broadcast-based name resolution, is that anyone on the same subnet can provide a malicious response to queries.  Not only that, Windows will often willingly provide your username and NTLMv2 password hash to whatever system it ends up communicating with.  And to make it worse, Google Chrome performs three random name queries when the application is launched.  Those random name queries very likely won't be resolved by DNS and will then be broadcast using LLMNR and NBNS.  So by simply opening Chrome, you might be sending your password hash to a malicious person on the network.  There are several tools which make this attack very easy to carry out: [Responder](https://github.com/lgandx/Responder) is an example.  You should always disable LLMNR and NetBIOS over TCP/IP if they are not needed.  The great part about the name poison detection built-in to tcpTrigger, is that it will work even if you have NetBIOS disabled.


#### Connection Notifier

Not interested in intrusion detection and you just want to know when someone is connecting to your important services?  Use tcpTrigger to monitor the ports you're intersted in and you're set.  Each time someone connects, you can have tcpTrigger display a popup message, send an email notification, or you can even kick off a script.


Download
--------
### [Click here to download latest .msi installer](https://github.com/R-Smith/tcpTrigger/releases/download/v1.1.0/tcpTrigger.Setup.msi)

### [Click here to download the source](https://github.com/R-Smith/tcpTrigger/archive/master.zip)

##### Notes
* .NET 3.5 or greater is required to run the service.
* .NET 4.5 or greater is required to run the graphical configuration editor.
* The installer does not do a prerequisites check, so make sure you have the required .NET frameworks.
* The pre-compiled installer is not code-signed, so you will get a scary warning when you run it.
* My build environment is Microsoft Visual Studio Community 2015 and WiX Toolset v3.10.
