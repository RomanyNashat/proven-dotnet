// Stands in for DailyReportHost.RunAsync (cronjob-patterns): Main's return value is the container's exit code,
// and the exit code is what Kubernetes reads to decide whether to retry the run.
return Environment.GetEnvironmentVariable("JOB_FAIL") == "1" ? 1 : 0;
