// Test PARAM_CHAR fix - should output "A :" (ASCII 65, 32, 58)
setvar $test1 #65
setvar $test2 #32
setvar $test3 #58
concat $result $test1 $test2
concat $result $result $test3
echo $result
