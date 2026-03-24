// Test SETTEXTOUTTRIGGER with & concatenation
SetTextOutTrigger test1 :label1 "simple"
SetTextOutTrigger test2 :label2 #27&"[A"
SetTextOutTrigger test3 :label3 "x"&"y"&"z"
echo "Triggers set successfully"
:label1
:label2
:label3
