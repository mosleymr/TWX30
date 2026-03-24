# Test SETDELAYTRIGGER with variable delay
systemscript
setvar $delay 5000
setdelaytrigger "mytrigger" $delay :mylabel
halt

:mylabel
setvar %triggered "yes"
