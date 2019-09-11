$loop = 1;
while ($loop)
{
    $process = Start-Process "dotnet fmbot.bot.dll" -Wait -NoNewWindow -PassThru
    switch ($process.ExitCode)
    {
        0 {"Exiting."; $loop = 0;}
        1 {"Restarting..."; Start-Sleep -s 3}
        default {"Unhandled Exit Code, Exiting."; $loop = 0}
    }
}