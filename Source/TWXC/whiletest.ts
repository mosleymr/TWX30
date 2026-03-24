setvar $i 1
while ($i <= 33)
        if (($custom_keys[$i] <> "0") AND ($custom_keys[$i] <> ""))
                if ($custom_keys[$i] = #9)
                        setVar $qss[$i] "TAB-TAB"
                elseif ($custom_keys[$i] = #13)
                        setVar $qss[$i] "TAB-Enter"
                elseif ($custom_keys[$i] = #8)
                        setVar $qss[$i] "TAB-Backspace"
                elseif ($custom_keys[$i] = #32)
                        setVar $qss[$i] "TAB-Spacebar"
                else
                        setVar $qss[$i] "TAB-"&$custom_keys[$i]
                end
        else
                setVar $qss[$i] "Undefined"
        end
        add $i 1
end
halt
