:start
setvar $major_version "1"
setvar $minor_version "2"
setvar $mbot_version $major_version&"."&$minor_version
savevar $mbot_version
echo $mbot_version
halt
