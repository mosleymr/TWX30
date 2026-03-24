# Test SETDELAYTRIGGER
systemscript
setdelaytrigger "mytrigger" 5000 :mylabel
halt

:mylabel
setvar %triggered "yes"
