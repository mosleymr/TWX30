:start
# Test concatenation operator with character codes
SetTextOutTrigger UpArrow2 :User_Access #27&"[A"
echo "Trigger set successfully"
halt

:User_Access
echo "UpArrow2 trigger activated"
halt
