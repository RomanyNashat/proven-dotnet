From SRE, pod clinic-pdf-7d9c (08:05 UTC):

    Limits:   cpu: 500m, memory: 512Mi
    Requests: cpu: 250m, memory: 512Mi
    Restart Count: 0

    $ cat /sys/fs/cgroup/cpu.stat   (08:03:00, then 08:04:00)
    nr_periods 41210        nr_periods 41810
    nr_throttled 9922       nr_throttled 10503
    throttled_usec 788120331 throttled_usec 816503922

Deployment env: DOTNET_gcServer=1 (set by the base chart for every service).
Since 08:00 the clinics have been printing the day's appointment letters (PDF), about 3x the usual load.
