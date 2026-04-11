LOGGING "OFF"
GOSUB :BOT~LOADVARS
LOADVAR $SHIP~CAP_FILE
LOADVAR $PLAYER~ONLYALIENS
LOADVAR $PLAYER~CAPPINGALIENS
LOADVAR $PLAYER~EMPTY_SHIPS_ONLY
LOADVAR $PLAYER~DEFENDERCAPPING
SETVAR $BOT~HELP[1] $BOT~TAB "cap   "
SETVAR $BOT~HELP[2] $BOT~TAB "    Captures enemy ships and attempts to not destroy them.   "
GOSUB :BOT~HELPFILE
GOSUB :COMBAT~INIT
LOADVAR $SHIP~CAP_FILE
FILEEXISTS $CAP_FILE_CHK $SHIP~CAP_FILE
if $CAP_FILE_CHK
  GOSUB :SHIP~LOADSHIPINFO
else
  GOSUB :SHIP~GETSHIPCAPSTATS
  GOSUB :SHIP~LOADSHIPINFO
end
:AUTOCAP
:CAP
GOSUB :PLAYER~QUIKSTATS
SETVAR $PLAYER~STARTINGLOCATION $PLAYER~CURRENT_PROMPT
if ($PLAYER~STARTINGLOCATION <> "Command")
  if ($PLAYER~STARTINGLOCATION = "Citadel")
    LOADVAR $BOT~MODE
    if ($BOT~MODE <> "Citcap")
      SETVAR $BOT~COMMAND "citcap"
      SETVAR $BOT~USER_COMMAND_LINE " citcap on "
      SETVAR $BOT~PARM1 "on"
      SAVEVAR $BOT~PARM1
      SAVEVAR $BOT~COMMAND
      SAVEVAR $BOT~USER_COMMAND_LINE
      SETVAR $BOT~MODE "Citcap"
      SAVEVAR $BOT~MODE
      LOAD "scripts\mombot\modes\offense\citcap.cts"
    else
      SETVAR $BOT~MODE "General"
      SAVEVAR $BOT~MODE
      STOP "scripts\mombot\modes\offense\citcap.cts"
      SETVAR $SWITCHBOARD~MESSAGE "Citcap off.*"
      GOSUB :SWITCHBOARD~SWITCHBOARD
    end
    HALT
  end
  SETVAR $SWITCHBOARD~MESSAGE "Wrong prompt for auto capture.*"
  GOSUB :SWITCHBOARD~SWITCHBOARD
  HALT
end
GETWORDPOS $BOT~USER_COMMAND_LINE $POS "alien"
if ($POS > 0)
  SETVAR $PLAYER~ONLYALIENS TRUE
else
  SETVAR $PLAYER~ONLYALIENS FALSE
end
FILEEXISTS $SHIP~CAP_FILE_CHK $SHIP~CAP_FILE
if ($SHIP~CAP_FILE_CHK <> TRUE)
  GOSUB :SHIP~GETSHIPCAPSTATS
end
LOADVAR $SHIP~SHIP_MAX_ATTACK
LOADVAR $SHIP~SHIP_FIGHTERS_MAX
LOADVAR $SHIP~SHIP_OFFENSIVE_ODDS
if ($SHIP~SHIP_OFFENSIVE_ODDS <= 0)
  GOSUB :SHIP~GETSHIPSTATS
end
SETVAR $LASTTARGET ""
SETVAR $THISTARGET ""
GOSUB :SECTOR~GETSECTORDATA
GOSUB :COMBAT~FASTCAPTURE
HALT
:BOT~LOADVARS
LOADVAR $BOT~MODE
LOADVAR $BOT~COMMAND
LOADVAR $SWITCHBOARD~BOT_NAME
SETVAR $BOT~BOT_NAME $SWITCHBOARD~BOT_NAME
LOADVAR $PLANET~PLANET_FILE
LOADVAR $SHIP~CAP_FILE
LOADVAR $BOT~USER_COMMAND_LINE
LOADVAR $BOT~PARM1
LOADVAR $BOT~PARM2
LOADVAR $BOT~PARM3
LOADVAR $BOT~PARM4
LOADVAR $BOT~PARM5
LOADVAR $BOT~PARM6
LOADVAR $BOT~PARM7
LOADVAR $BOT~PARM8
LOADVAR $BOT~BOT_TURN_LIMIT
LOADVAR $PLAYER~UNLIMITEDGAME
LOADVAR $MAP~STARDOCK
LOADVAR $MAP~RYLOS
LOADVAR $MAP~ALPHA_CENTAURI
LOADVAR $MAP~HOME_SECTOR
LOADVAR $MAP~BACKDOOR
LOADVAR $BOT~SILENT_RUNNING
LOADVAR $BOT~BOTISDEAF
LOADVAR $SWITCHBOARD~SELF_COMMAND
LOADVAR $PLANET~PLANET
LOADVAR $BOT~PASSWORD
LOADVAR $BOT~LETTER
LOADVAR $GAME~PORT_MAX
LOADVAR $BOT~FOLDER
LOADVAR $GAME~PHOTON_DURATION
SETARRAY $BOT~HELP 60
SETVAR $BOT~HELP 60
SETVAR $BOT~TAB "     "
RETURN
:BOT~HELPFILE
MERGETEXT $BOT~COMMAND ".txt" $BOT~$3
MERGETEXT "scripts\mombot\help\" $BOT~$3 $BOT~$1
SETVAR $BOT~HELP_FILE $BOT~$1
FILEEXISTS $BOT~DOESHELPFILEEXIST $BOT~HELP_FILE
SETVAR $BOT~ONLY_HELP FALSE
ISEQUAL $BOT~$2 $BOT~PARM1 "help"
ISEQUAL $BOT~$5 $BOT~PARM1 "?"
SETVAR $BOT~$1 $BOT~$2
OR $BOT~$1 $BOT~$5
if $BOT~$1
  SETVAR $BOT~ONLY_HELP TRUE
:BOT~:15
:BOT~:16
  if $BOT~DOESHELPFILEEXIST
    SETVAR $BOT~I 1
    SETVAR $BOT~$1 $BOT~I
    ADD $BOT~$1 4
    READ $BOT~HELP_FILE $BOT~HELP_LINE $BOT~$1
:BOT~:19
    ISNOTEQUAL $BOT~$1 $BOT~HELP_LINE "EOF"
    if $BOT~$1
      STRIPTEXT $BOT~HELP[$BOT~I] #13
      STRIPTEXT $BOT~HELP[$BOT~I] "`"
      STRIPTEXT $BOT~HELP[$BOT~I] "'"
      REPLACETEXT $BOT~HELP[$BOT~I] "=" "-"
      ISNOTEQUAL $BOT~$1 $BOT~HELP[$BOT~I] $BOT~HELP_LINE
      if $BOT~$1
        goto :WRITE_NEW_HELP_FILE
:BOT~:21
:BOT~:22
        ADD $BOT~I 1
        SETVAR $BOT~$1 $BOT~I
        ADD $BOT~$1 4
        READ $BOT~HELP_FILE $BOT~HELP_LINE $BOT~$1
:BOT~:20
        SETVAR $BOT~$5 $BOT~I
        ADD $BOT~$5 1
        ISNOTEQUAL $BOT~$2 $BOT~HELP[$BOT~$5] 0
        SETVAR $BOT~$11 $BOT~I
        ADD $BOT~$11 2
        ISNOTEQUAL $BOT~$8 $BOT~HELP[$BOT~$11] 0
        SETVAR $BOT~$1 $BOT~$2
        OR $BOT~$1 $BOT~$8
        if $BOT~$1
          goto :WRITE_NEW_HELP_FILE
:BOT~:23
:BOT~:24
          ISEQUAL $BOT~$1 $BOT~ONLY_HELP TRUE
          if $BOT~$1
            GOSUB :DISPLAYHELP
            HALT
:BOT~:25
:BOT~:26
            RETURN
:BOT~:17
:BOT~:18
:BOT~WRITE_NEW_HELP_FILE
            DELETE $BOT~HELP_FILE
            SETVAR $BOT~I 1
            GETLENGTH $BOT~COMMAND $BOT~LENGTH
            SETVAR $BOT~SPACES "                                            "
            SETVAR $BOT~STARS "---------------------------------------------"
            SETVAR $BOT~POS $BOT~LENGTH
            CUTTEXT $BOT~STARS $BOT~BORDER 1 $BOT~POS
            SETVAR $BOT~$4 $BOT~LENGTH
            ADD $BOT~$4 10
            SETVAR $BOT~$2 50
            SUBTRACT $BOT~$2 $BOT~$4
            SETVAR $BOT~$1 $BOT~$2
            DIVIDE $BOT~$1 2
            SETVAR $BOT~POS $BOT~$1
            CUTTEXT $BOT~SPACES $BOT~CENTER 1 $BOT~POS
            WRITE $BOT~HELP_FILE "                     "
            WRITE $BOT~HELP_FILE "   "
            MERGETEXT $BOT~COMMAND " >>>>" $BOT~$5
            MERGETEXT "<<<< " $BOT~$5 $BOT~$3
            MERGETEXT $BOT~CENTER $BOT~$3 $BOT~$1
            WRITE $BOT~HELP_FILE $BOT~$1
            WRITE $BOT~HELP_FILE "   "
:BOT~:27
            ISLESSEREQUAL $BOT~$1 $BOT~I $BOT~HELP
            if $BOT~$1
              STRIPTEXT $BOT~HELP[$BOT~I] #13
              STRIPTEXT $BOT~HELP[$BOT~I] "`"
              STRIPTEXT $BOT~HELP[$BOT~I] "'"
              REPLACETEXT $BOT~HELP[$BOT~I] "=" "-"
              ISEQUAL $BOT~$1 $BOT~HELP[$BOT~I] 0
              if $BOT~$1
                goto :DONE_HELP_FILE
:BOT~:29
:BOT~:30
                WRITE $BOT~HELP_FILE $BOT~HELP[$BOT~I]
                ADD $BOT~I 1
:BOT~:28
:BOT~DONE_HELP_FILE
                MERGETEXT $BOT~COMMAND " in help directory.*" $BOT~$3
                MERGETEXT "Writing text file for " $BOT~$3 $BOT~$1
                SETVAR $SWITCHBOARD~MESSAGE $BOT~$1
                GOSUB :SWITCHBOARD~SWITCHBOARD
                ISEQUAL $BOT~$1 $BOT~ONLY_HELP TRUE
                if $BOT~$1
                  GOSUB :DISPLAYHELP
                  HALT
:BOT~:31
:BOT~:32
                  RETURN
:BOT~DISPLAYHELP
                  SETVAR $BOT~I 1
                  SETVAR $BOT~HELPOUTPUT ""
                  SETVAR $BOT~ISDONE FALSE
:BOT~:33
                  ISLESSEREQUAL $BOT~$2 $BOT~I $BOT~HELP
                  ISNOTEQUAL $BOT~$5 $BOT~ISDONE TRUE
                  SETVAR $BOT~$1 $BOT~$2
                  AND $BOT~$1 $BOT~$5
                  if $BOT~$1
                    ISNOTEQUAL $BOT~$1 $BOT~HELP[$BOT~I] 0
                    if $BOT~$1
                      STRIPTEXT $BOT~HELP[$BOT~I] #13
                      STRIPTEXT $BOT~HELP[$BOT~I] "`"
                      STRIPTEXT $BOT~HELP[$BOT~I] "'"
                      REPLACETEXT $BOT~HELP[$BOT~I] "=" "-"
                      SETVAR $BOT~TEMP $BOT~HELP[$BOT~I]
                      GETLENGTH $BOT~TEMP $BOT~LENGTH
                      SETVAR $BOT~ISTOOLONG FALSE
                      SETVAR $BOT~NEXT_LINE ""
                      SETVAR $BOT~MAX_LENGTH 65
                      ISEQUAL $BOT~$2 $SWITCHBOARD~SELF_COMMAND TRUE
                      ISEQUAL $BOT~$5 $BOT~SILENT_RUNNING TRUE
                      SETVAR $BOT~$1 $BOT~$2
                      OR $BOT~$1 $BOT~$5
                      if $BOT~$1
                        SETVAR $BOT~LINE $BOT~HELP[$BOT~I]
                        GOSUB :FORMATHELPLINE
                        SETVAR $BOT~HELP[$BOT~I] $BOT~LINE
                        SETVAR $BOT~NEXT_LINE_TEST $BOT~NEXT_LINE
                        STRIPTEXT $BOT~NEXT_LINE_TEST " "
                        ISNOTEQUAL $BOT~$1 $BOT~NEXT_LINE_TEST ""
                        if $BOT~$1
                          SETVAR $BOT~LINE $BOT~NEXT_LINE
                          GOSUB :FORMATHELPLINE
                          SETVAR $BOT~NEXT_LINE $BOT~LINE
:BOT~:39
:BOT~:40
:BOT~:37
:BOT~:41
                          ISGREATER $BOT~$1 $BOT~LENGTH $BOT~MAX_LENGTH
                          if $BOT~$1
                            SETVAR $BOT~ISTOOLONG TRUE
                            SETVAR $BOT~$1 $BOT~MAX_LENGTH
                            ADD $BOT~$1 1
                            SETVAR $BOT~$4 $BOT~LENGTH
                            SUBTRACT $BOT~$4 $BOT~MAX_LENGTH
                            CUTTEXT $BOT~TEMP $BOT~NEXT_LINE $BOT~$1 $BOT~$4
                            CUTTEXT $BOT~TEMP $BOT~HELP[$BOT~I] 1 $BOT~MAX_LENGTH
                            GETLENGTH $BOT~NEXT_LINE $BOT~LENGTH
:BOT~:42
:BOT~:37
:BOT~:38
                            MERGETEXT $BOT~HELP[$BOT~I] "  *" $BOT~$3
                            MERGETEXT $BOT~HELPOUTPUT $BOT~$3 $BOT~$1
                            SETVAR $BOT~HELPOUTPUT $BOT~$1
                            SETVAR $BOT~NEXT_LINE_TEST $BOT~NEXT_LINE
                            STRIPTEXT $BOT~NEXT_LINE_TEST " "
                            ISNOTEQUAL $BOT~$1 $BOT~NEXT_LINE_TEST ""
                            if $BOT~$1
                              MERGETEXT $BOT~NEXT_LINE "  *" $BOT~$5
                              MERGETEXT "" $BOT~$5 $BOT~$3
                              MERGETEXT $BOT~HELPOUTPUT $BOT~$3 $BOT~$1
                              SETVAR $BOT~HELPOUTPUT $BOT~$1
:BOT~:43
:BOT~:44
                              ISLESSEREQUAL $BOT~$1 $BOT~LENGTH 1
                              if $BOT~$1
:BOT~:45
:BOT~:46
:BOT~:35
                                SETVAR $BOT~ISDONE TRUE
:BOT~:35
:BOT~:36
                                ADD $BOT~I 1
:BOT~:34
                                ISEQUAL $BOT~$2 $SWITCHBOARD~SELF_COMMAND TRUE
                                ISEQUAL $BOT~$5 $BOT~SILENT_RUNNING TRUE
                                SETVAR $BOT~$1 $BOT~$2
                                OR $BOT~$1 $BOT~$5
                                if $BOT~$1
                                  MERGETEXT "  *     *-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-*" ANSI_15 $BOT~$13
                                  MERGETEXT ANSI_14 $BOT~$13 $BOT~$11
                                  MERGETEXT $BOT~HELPOUTPUT $BOT~$11 $BOT~$9
                                  MERGETEXT ANSI_15 $BOT~$9 $BOT~$7
                                  MERGETEXT "-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-*  *" $BOT~$7 $BOT~$5
                                  MERGETEXT ANSI_14 $BOT~$5 $BOT~$3
                                  MERGETEXT "  *" $BOT~$3 $BOT~$1
                                  SETVAR $BOT~HELPOUTPUT $BOT~$1
                                  SETVAR $SWITCHBOARD~MESSAGE $BOT~HELPOUTPUT
                                  GOSUB :SWITCHBOARD~SWITCHBOARD
:BOT~:47
                                  MERGETEXT $BOT~HELPOUTPUT "  *     *-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-*" $BOT~$5
                                  MERGETEXT "-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-*" $BOT~$5 $BOT~$3
                                  MERGETEXT "  *" $BOT~$3 $BOT~$1
                                  SETVAR $BOT~HELPOUTPUT $BOT~$1
                                  MERGETEXT $BOT~HELPOUTPUT "*" $BOT~$7
                                  MERGETEXT "} - *" $BOT~$7 $BOT~$5
                                  MERGETEXT $SWITCHBOARD~BOT_NAME $BOT~$5 $BOT~$3
                                  MERGETEXT "'*{" $BOT~$3 $BOT~$1
                                  SEND $BOT~$1
:BOT~:47
:BOT~:48
                                  RETURN
:BOT~FORMATHELPLINE
                                  MERGETEXT "[" ANSI_6 $BOT~$3
                                  MERGETEXT ANSI_2 $BOT~$3 $BOT~$1
                                  REPLACETEXT $BOT~LINE "[" $BOT~$1
                                  MERGETEXT "]" ANSI_13 $BOT~$3
                                  MERGETEXT ANSI_2 $BOT~$3 $BOT~$1
                                  REPLACETEXT $BOT~LINE "]" $BOT~$1
                                  MERGETEXT "-" ANSI_13 $BOT~$3
                                  MERGETEXT ANSI_7 $BOT~$3 $BOT~$1
                                  REPLACETEXT $BOT~LINE "-" $BOT~$1
                                  MERGETEXT "<" ANSI_15 $BOT~$15
                                  MERGETEXT ANSI_7 $BOT~$15 $BOT~$13
                                  MERGETEXT "<" $BOT~$13 $BOT~$11
                                  MERGETEXT ANSI_14 $BOT~$11 $BOT~$9
                                  MERGETEXT "<" $BOT~$9 $BOT~$7
                                  MERGETEXT ANSI_7 $BOT~$7 $BOT~$5
                                  MERGETEXT "<" $BOT~$5 $BOT~$3
                                  MERGETEXT ANSI_14 $BOT~$3 $BOT~$1
                                  REPLACETEXT $BOT~LINE "<<<<" $BOT~$1
                                  MERGETEXT ANSI_14 ">" $BOT~$13
                                  MERGETEXT ">" $BOT~$13 $BOT~$11
                                  MERGETEXT ANSI_7 $BOT~$11 $BOT~$9
                                  MERGETEXT ">" $BOT~$9 $BOT~$7
                                  MERGETEXT ANSI_14 $BOT~$7 $BOT~$5
                                  MERGETEXT ">" $BOT~$5 $BOT~$3
                                  MERGETEXT ANSI_7 $BOT~$3 $BOT~$1
                                  REPLACETEXT $BOT~LINE ">>>>" $BOT~$1
                                  MERGETEXT "{" ANSI_6 $BOT~$3
                                  MERGETEXT ANSI_2 $BOT~$3 $BOT~$1
                                  REPLACETEXT $BOT~LINE "{" $BOT~$1
                                  MERGETEXT "}" ANSI_13 $BOT~$3
                                  MERGETEXT ANSI_2 $BOT~$3 $BOT~$1
                                  REPLACETEXT $BOT~LINE "}" $BOT~$1
                                  MERGETEXT : ANSI_13 $BOT~$7
                                  MERGETEXT ANSI_2 $BOT~$7 $BOT~$5
                                  MERGETEXT "Options" $BOT~$5 $BOT~$3
                                  MERGETEXT ANSI_6 $BOT~$3 $BOT~$1
                                  REPLACETEXT $BOT~LINE "Options:" $BOT~$1
                                  MERGETEXT $BOT~LINE ANSI_15 $BOT~$3
                                  MERGETEXT ANSI_13 $BOT~$3 $BOT~$1
                                  SETVAR $BOT~LINE $BOT~$1
                                  RETURN
:SWITCHBOARD~SWITCHBOARD
                                  SETVAR $SWITCHBOARD~DISCORD_IGNORE "-- "
                                  SETVAR $SWITCHBOARD~DISCORD_IGNORE_LENGTH 3
                                  LOADVAR $BOT~BOTISDEAF
                                  LOADVAR $BOT~MODE
                                  LOADVAR $SWITCHBOARD~NODISCORD
                                  LOADVAR $SWITCHBOARD~FEDSPACE_OUTPUT
                                  ISNOTEQUAL $SWITCHBOARD~$1 $SWITCHBOARD~NODISCORD TRUE
                                  if $SWITCHBOARD~$1
                                    MERGETEXT $BOT~USER_COMMAND_LINE " " $SWITCHBOARD~$3
                                    MERGETEXT " " $SWITCHBOARD~$3 $SWITCHBOARD~$1
                                    GETWORDPOS $SWITCHBOARD~$1 $SWITCHBOARD~POS " nodiscord "
                                    ISGREATER $SWITCHBOARD~$1 $SWITCHBOARD~POS 0
                                    if $SWITCHBOARD~$1
                                      SETVAR $SWITCHBOARD~NODISCORD TRUE
:SWITCHBOARD~:51
:SWITCHBOARD~:52
:SWITCHBOARD~:49
:SWITCHBOARD~:50
                                      ISNOTEQUAL $SWITCHBOARD~$1 $SWITCHBOARD~FEDSPACE_OUTPUT TRUE
                                      if $SWITCHBOARD~$1
                                        MERGETEXT $BOT~USER_COMMAND_LINE " " $SWITCHBOARD~$3
                                        MERGETEXT " " $SWITCHBOARD~$3 $SWITCHBOARD~$1
                                        GETWORDPOS $SWITCHBOARD~$1 $SWITCHBOARD~POS " fed "
                                        ISGREATER $SWITCHBOARD~$1 $SWITCHBOARD~POS 0
                                        if $SWITCHBOARD~$1
                                          SETVAR $SWITCHBOARD~FEDSPACE_OUTPUT TRUE
:SWITCHBOARD~:55
:SWITCHBOARD~:56
:SWITCHBOARD~:53
:SWITCHBOARD~:54
                                          if $SWITCHBOARD~FEDSPACE_OUTPUT
                                            SETVAR $SWITCHBOARD~COMMUNICATION_STARTER "`"
                                            if $SWITCHBOARD~NODISCORD
                                              MERGETEXT $SWITCHBOARD~DISCORD_IGNORE "Fedspace output - " $SWITCHBOARD~$3
                                              MERGETEXT $SWITCHBOARD~COMMUNICATION_STARTER $SWITCHBOARD~$3 $SWITCHBOARD~$1
                                              SETVAR $SWITCHBOARD~MSG_HEADER_SS_1 $SWITCHBOARD~$1
                                              MERGETEXT $SWITCHBOARD~DISCORD_IGNORE "Fedspace output - *" $SWITCHBOARD~$5
                                              MERGETEXT "*" $SWITCHBOARD~$5 $SWITCHBOARD~$3
                                              MERGETEXT $SWITCHBOARD~COMMUNICATION_STARTER $SWITCHBOARD~$3 $SWITCHBOARD~$1
                                              SETVAR $SWITCHBOARD~MSG_HEADER_SS_2 $SWITCHBOARD~$1
:SWITCHBOARD~:59
                                              MERGETEXT $SWITCHBOARD~COMMUNICATION_STARTER "Fedspace output - " $SWITCHBOARD~$1
                                              SETVAR $SWITCHBOARD~MSG_HEADER_SS_1 $SWITCHBOARD~$1
                                              MERGETEXT $SWITCHBOARD~COMMUNICATION_STARTER "*Fedspace output - *" $SWITCHBOARD~$1
                                              SETVAR $SWITCHBOARD~MSG_HEADER_SS_2 $SWITCHBOARD~$1
:SWITCHBOARD~:59
:SWITCHBOARD~:60
:SWITCHBOARD~:57
                                              SETVAR $SWITCHBOARD~COMMUNICATION_STARTER "'"
                                              if $SWITCHBOARD~NODISCORD
                                                MERGETEXT $SWITCHBOARD~BOT_NAME "} - " $SWITCHBOARD~$11
                                                MERGETEXT "] {" $SWITCHBOARD~$11 $SWITCHBOARD~$9
                                                MERGETEXT $BOT~MODE $SWITCHBOARD~$9 $SWITCHBOARD~$7
                                                MERGETEXT "[" $SWITCHBOARD~$7 $SWITCHBOARD~$5
                                                MERGETEXT $SWITCHBOARD~DISCORD_IGNORE $SWITCHBOARD~$5 $SWITCHBOARD~$3
                                                MERGETEXT $SWITCHBOARD~COMMUNICATION_STARTER $SWITCHBOARD~$3 $SWITCHBOARD~$1
                                                SETVAR $SWITCHBOARD~MSG_HEADER_SS_1 $SWITCHBOARD~$1
                                                MERGETEXT $SWITCHBOARD~BOT_NAME "} - *" $SWITCHBOARD~$13
                                                MERGETEXT "] {" $SWITCHBOARD~$13 $SWITCHBOARD~$11
                                                MERGETEXT $BOT~MODE $SWITCHBOARD~$11 $SWITCHBOARD~$9
                                                MERGETEXT "[" $SWITCHBOARD~$9 $SWITCHBOARD~$7
                                                MERGETEXT $SWITCHBOARD~DISCORD_IGNORE $SWITCHBOARD~$7 $SWITCHBOARD~$5
                                                MERGETEXT "*" $SWITCHBOARD~$5 $SWITCHBOARD~$3
                                                MERGETEXT $SWITCHBOARD~COMMUNICATION_STARTER $SWITCHBOARD~$3 $SWITCHBOARD~$1
                                                SETVAR $SWITCHBOARD~MSG_HEADER_SS_2 $SWITCHBOARD~$1
:SWITCHBOARD~:61
                                                MERGETEXT $SWITCHBOARD~BOT_NAME "} - " $SWITCHBOARD~$9
                                                MERGETEXT "] {" $SWITCHBOARD~$9 $SWITCHBOARD~$7
                                                MERGETEXT $BOT~MODE $SWITCHBOARD~$7 $SWITCHBOARD~$5
                                                MERGETEXT "[" $SWITCHBOARD~$5 $SWITCHBOARD~$3
                                                MERGETEXT $SWITCHBOARD~COMMUNICATION_STARTER $SWITCHBOARD~$3 $SWITCHBOARD~$1
                                                SETVAR $SWITCHBOARD~MSG_HEADER_SS_1 $SWITCHBOARD~$1
                                                MERGETEXT $SWITCHBOARD~BOT_NAME "} - *" $SWITCHBOARD~$9
                                                MERGETEXT "] {" $SWITCHBOARD~$9 $SWITCHBOARD~$7
                                                MERGETEXT $BOT~MODE $SWITCHBOARD~$7 $SWITCHBOARD~$5
                                                MERGETEXT "*[" $SWITCHBOARD~$5 $SWITCHBOARD~$3
                                                MERGETEXT $SWITCHBOARD~COMMUNICATION_STARTER $SWITCHBOARD~$3 $SWITCHBOARD~$1
                                                SETVAR $SWITCHBOARD~MSG_HEADER_SS_2 $SWITCHBOARD~$1
:SWITCHBOARD~:61
:SWITCHBOARD~:62
:SWITCHBOARD~:57
:SWITCHBOARD~:58
                                                MERGETEXT "} " ANSI_15 $SWITCHBOARD~$11
                                                MERGETEXT ANSI_9 $SWITCHBOARD~$11 $SWITCHBOARD~$9
                                                MERGETEXT $SWITCHBOARD~BOT_NAME $SWITCHBOARD~$9 $SWITCHBOARD~$7
                                                MERGETEXT ANSI_14 $SWITCHBOARD~$7 $SWITCHBOARD~$5
                                                MERGETEXT "{" $SWITCHBOARD~$5 $SWITCHBOARD~$3
                                                MERGETEXT ANSI_9 $SWITCHBOARD~$3 $SWITCHBOARD~$1
                                                SETVAR $SWITCHBOARD~MSG_HEADER_ECHO $SWITCHBOARD~$1
                                                ISNOTEQUAL $SWITCHBOARD~$1 $SWITCHBOARD~MESSAGE ""
                                                if $SWITCHBOARD~$1
                                                  ISGREATER $SWITCHBOARD~$1 $SWITCHBOARD~SELF_COMMAND 0
                                                  if $SWITCHBOARD~$1
                                                    SETVAR $SWITCHBOARD~LENGTH 0
:SWITCHBOARD~:65
                                                    GETLENGTH $SWITCHBOARD~BOT_NAME $SWITCHBOARD~LENGTH
:SWITCHBOARD~:65
:SWITCHBOARD~:66
                                                    SETVAR $SWITCHBOARD~I 1
                                                    SETVAR $SWITCHBOARD~SPACING ""
                                                    MERGETEXT $BOT~USER_COMMAND_LINE " " $SWITCHBOARD~$3
                                                    MERGETEXT " " $SWITCHBOARD~$3 $SWITCHBOARD~$1
                                                    GETWORDPOS $SWITCHBOARD~$1 $SWITCHBOARD~ISBROADCAST " ss "
                                                    MERGETEXT $BOT~USER_COMMAND_LINE " " $SWITCHBOARD~$3
                                                    MERGETEXT " " $SWITCHBOARD~$3 $SWITCHBOARD~$1
                                                    GETWORDPOS $SWITCHBOARD~$1 $SWITCHBOARD~ISSILENT " silent "
                                                    ISNOTEQUAL $SWITCHBOARD~$1 $SWITCHBOARD~SELF_COMMAND 0
                                                    if $SWITCHBOARD~$1
                                                      ISNOTEQUAL $SWITCHBOARD~$2 $BOT~COMMAND "help"
                                                      ISNOTEQUAL $SWITCHBOARD~$5 $BOT~ONLY_HELP TRUE
                                                      SETVAR $SWITCHBOARD~$1 $SWITCHBOARD~$2
                                                      AND $SWITCHBOARD~$1 $SWITCHBOARD~$5
                                                      if $SWITCHBOARD~$1
                                                        ISGREATER $SWITCHBOARD~$2 $SWITCHBOARD~SELF_COMMAND 1
                                                        ISEQUAL $SWITCHBOARD~$6 $SWITCHBOARD~SELF_COMMAND 1
                                                        ISNOTEQUAL $SWITCHBOARD~$10 $BOT~SILENT_RUNNING TRUE
                                                        ISLESSEREQUAL $SWITCHBOARD~$13 $SWITCHBOARD~ISSILENT 0
                                                        SETVAR $SWITCHBOARD~$9 $SWITCHBOARD~$10
                                                        AND $SWITCHBOARD~$9 $SWITCHBOARD~$13
                                                        SETVAR $SWITCHBOARD~$5 $SWITCHBOARD~$6
                                                        AND $SWITCHBOARD~$5 $SWITCHBOARD~$9
                                                        SETVAR $SWITCHBOARD~$1 $SWITCHBOARD~$2
                                                        OR $SWITCHBOARD~$1 $SWITCHBOARD~$5
                                                        if $SWITCHBOARD~$1
                                                          STRIPTEXT $SWITCHBOARD~MESSAGE ANSI_1
                                                          STRIPTEXT $SWITCHBOARD~MESSAGE ANSI_2
                                                          STRIPTEXT $SWITCHBOARD~MESSAGE ANSI_3
                                                          STRIPTEXT $SWITCHBOARD~MESSAGE ANSI_4
                                                          STRIPTEXT $SWITCHBOARD~MESSAGE ANSI_5
                                                          STRIPTEXT $SWITCHBOARD~MESSAGE ANSI_6
                                                          STRIPTEXT $SWITCHBOARD~MESSAGE ANSI_7
                                                          STRIPTEXT $SWITCHBOARD~MESSAGE ANSI_8
                                                          STRIPTEXT $SWITCHBOARD~MESSAGE ANSI_9
                                                          STRIPTEXT $SWITCHBOARD~MESSAGE ANSI_10
                                                          STRIPTEXT $SWITCHBOARD~MESSAGE ANSI_11
                                                          STRIPTEXT $SWITCHBOARD~MESSAGE ANSI_12
                                                          STRIPTEXT $SWITCHBOARD~MESSAGE ANSI_13
                                                          STRIPTEXT $SWITCHBOARD~MESSAGE ANSI_14
                                                          STRIPTEXT $SWITCHBOARD~MESSAGE ANSI_15
                                                          ISNOTEQUAL $SWITCHBOARD~$1 $SWITCHBOARD~HELPLIST TRUE
                                                          if $SWITCHBOARD~$1
:SWITCHBOARD~:73
:SWITCHBOARD~:74
:SWITCHBOARD~:71
:SWITCHBOARD~:72
:SWITCHBOARD~:69
:SWITCHBOARD~:70
:SWITCHBOARD~:75
                                                            ISLESSEREQUAL $SWITCHBOARD~$1 $SWITCHBOARD~I $SWITCHBOARD~LENGTH
                                                            if $SWITCHBOARD~$1
                                                              MERGETEXT $SWITCHBOARD~SPACING " " $SWITCHBOARD~$1
                                                              SETVAR $SWITCHBOARD~SPACING $SWITCHBOARD~$1
                                                              ADD $SWITCHBOARD~I 1
:SWITCHBOARD~:76
                                                              SETVAR $SWITCHBOARD~NEW_MESSAGE ""
                                                              SETVAR $SWITCHBOARD~MESSAGE_LINE ""
                                                              GOSUB :FORMAT_RAW_MESSAGE
:SWITCHBOARD~:67
                                                              GOSUB :FORMAT_RAW_MESSAGE
:SWITCHBOARD~:67
:SWITCHBOARD~:68
                                                              MERGETEXT $SWITCHBOARD~NEW_MESSAGE " " $SWITCHBOARD~$3
                                                              MERGETEXT " " $SWITCHBOARD~$3 $SWITCHBOARD~$1
                                                              GETWORDPOS $SWITCHBOARD~$1 $SWITCHBOARD~POS "*"
                                                              GETLENGTH $SWITCHBOARD~NEW_MESSAGE $SWITCHBOARD~LENGTH
                                                              if $SWITCHBOARD~NODISCORD
                                                                MERGETEXT $SWITCHBOARD~DISCORD_IGNORE $SWITCHBOARD~NEW_MESSAGE $SWITCHBOARD~$1
                                                                SETVAR $SWITCHBOARD~NEW_MESSAGE $SWITCHBOARD~$1
                                                                ADD $SWITCHBOARD~LENGTH $SWITCHBOARD~DISCORD_IGNORE_LENGTH
:SWITCHBOARD~:77
:SWITCHBOARD~:78
                                                                ISGREATER $SWITCHBOARD~$1 $SWITCHBOARD~SELF_COMMAND 1
                                                                if $SWITCHBOARD~$1
                                                                  SETVAR $SWITCHBOARD~SELF_COMMAND FALSE
:SWITCHBOARD~:79
:SWITCHBOARD~:80
                                                                  ISLESSER $SWITCHBOARD~$1 $SWITCHBOARD~POS $SWITCHBOARD~LENGTH
                                                                  if $SWITCHBOARD~$1
                                                                    SETVAR $SWITCHBOARD~MULTIPLE_LINES TRUE
:SWITCHBOARD~:81
                                                                    SETVAR $SWITCHBOARD~MULTIPLE_LINES FALSE
:SWITCHBOARD~:81
:SWITCHBOARD~:82
                                                                    ISGREATER $SWITCHBOARD~$3 $SWITCHBOARD~ISSILENT 0
                                                                    ISEQUAL $SWITCHBOARD~$7 $BOT~SILENT_RUNNING TRUE
                                                                    ISEQUAL $SWITCHBOARD~$10 $SWITCHBOARD~SELF_COMMAND TRUE
                                                                    SETVAR $SWITCHBOARD~$6 $SWITCHBOARD~$7
                                                                    AND $SWITCHBOARD~$6 $SWITCHBOARD~$10
                                                                    SETVAR $SWITCHBOARD~$2 $SWITCHBOARD~$3
                                                                    OR $SWITCHBOARD~$2 $SWITCHBOARD~$6
                                                                    ISEQUAL $SWITCHBOARD~$15 $SWITCHBOARD~SELF_COMMAND TRUE
                                                                    ISEQUAL $SWITCHBOARD~$19 $BOT~COMMAND "help"
                                                                    ISEQUAL $SWITCHBOARD~$22 $BOT~ONLY_HELP TRUE
                                                                    SETVAR $SWITCHBOARD~$18 $SWITCHBOARD~$19
                                                                    OR $SWITCHBOARD~$18 $SWITCHBOARD~$22
                                                                    SETVAR $SWITCHBOARD~$14 $SWITCHBOARD~$15
                                                                    AND $SWITCHBOARD~$14 $SWITCHBOARD~$18
                                                                    ISLESSEREQUAL $SWITCHBOARD~$25 $SWITCHBOARD~ISBROADCAST 0
                                                                    SETVAR $SWITCHBOARD~$13 $SWITCHBOARD~$14
                                                                    AND $SWITCHBOARD~$13 $SWITCHBOARD~$25
                                                                    SETVAR $SWITCHBOARD~$1 $SWITCHBOARD~$2
                                                                    OR $SWITCHBOARD~$1 $SWITCHBOARD~$13
                                                                    if $SWITCHBOARD~$1
                                                                      ISNOTEQUAL $SWITCHBOARD~$1 $BOT~BOTISDEAF TRUE
                                                                      if $SWITCHBOARD~$1
                                                                        MERGETEXT $SWITCHBOARD~MSG_HEADER_ECHO $SWITCHBOARD~NEW_MESSAGE $SWITCHBOARD~$3
                                                                        MERGETEXT "*" $SWITCHBOARD~$3 $SWITCHBOARD~$1
                                                                        ECHO $SWITCHBOARD~$1
                                                                        SEND #145
:SWITCHBOARD~:85
                                                                        SETVAR $SWITCHBOARD~WINDOW_CONTENT $SWITCHBOARD~NEW_MESSAGE
                                                                        REPLACETEXT $SWITCHBOARD~WINDOW_CONTENT "*" "[][]"
                                                                        SAVEVAR $SWITCHBOARD~WINDOW_CONTENT
:SWITCHBOARD~:85
:SWITCHBOARD~:86
:SWITCHBOARD~:83
                                                                        ISEQUAL $SWITCHBOARD~$1 $SWITCHBOARD~MULTIPLE_LINES FALSE
                                                                        if $SWITCHBOARD~$1
                                                                          MERGETEXT $SWITCHBOARD~MSG_HEADER_SS_1 $SWITCHBOARD~NEW_MESSAGE $SWITCHBOARD~$1
                                                                          SEND $SWITCHBOARD~$1
:SWITCHBOARD~:87
                                                                          MERGETEXT $SWITCHBOARD~NEW_MESSAGE "*" $SWITCHBOARD~$3
                                                                          MERGETEXT $SWITCHBOARD~MSG_HEADER_SS_2 $SWITCHBOARD~$3 $SWITCHBOARD~$1
                                                                          SEND $SWITCHBOARD~$1
:SWITCHBOARD~:87
:SWITCHBOARD~:84
                                                                          SETVAR $SWITCHBOARD~MESSAGE ""
:SWITCHBOARD~:63
:SWITCHBOARD~:64
                                                                          SETVAR $SWITCHBOARD~HELPLIST FALSE
                                                                          RETURN
:SWITCHBOARD~FORMAT_RAW_MESSAGE
                                                                          MERGETEXT $SWITCHBOARD~MESSAGE " " $SWITCHBOARD~$3
                                                                          MERGETEXT " " $SWITCHBOARD~$3 $SWITCHBOARD~$1
                                                                          GETWORDPOS $SWITCHBOARD~$1 $SWITCHBOARD~POS "*"
                                                                          GETLENGTH $SWITCHBOARD~MESSAGE $SWITCHBOARD~MESSAGE_LENGTH
                                                                          ISLESSER $SWITCHBOARD~$1 $SWITCHBOARD~POS $SWITCHBOARD~MESSAGE_LENGTH
                                                                          if $SWITCHBOARD~$1
                                                                            SETVAR $SWITCHBOARD~MULTIPLE_LINES TRUE
:SWITCHBOARD~:88
                                                                            SETVAR $SWITCHBOARD~MULTIPLE_LINES FALSE
:SWITCHBOARD~:88
:SWITCHBOARD~:89
                                                                            ISNOTEQUAL $SWITCHBOARD~$2 $BOT~COMMAND "help"
                                                                            ISNOTEQUAL $SWITCHBOARD~$5 $BOT~ONLY_HELP TRUE
                                                                            SETVAR $SWITCHBOARD~$1 $SWITCHBOARD~$2
                                                                            AND $SWITCHBOARD~$1 $SWITCHBOARD~$5
                                                                            if $SWITCHBOARD~$1
                                                                              ISEQUAL $SWITCHBOARD~$2 $SWITCHBOARD~SELF_COMMAND 0
                                                                              ISGREATER $SWITCHBOARD~$6 $SWITCHBOARD~SELF_COMMAND 1
                                                                              ISEQUAL $SWITCHBOARD~$10 $SWITCHBOARD~SELF_COMMAND 1
                                                                              ISNOTEQUAL $SWITCHBOARD~$14 $BOT~SILENT_RUNNING TRUE
                                                                              ISLESSEREQUAL $SWITCHBOARD~$17 $SWITCHBOARD~ISSILENT 0
                                                                              SETVAR $SWITCHBOARD~$13 $SWITCHBOARD~$14
                                                                              AND $SWITCHBOARD~$13 $SWITCHBOARD~$17
                                                                              SETVAR $SWITCHBOARD~$9 $SWITCHBOARD~$10
                                                                              AND $SWITCHBOARD~$9 $SWITCHBOARD~$13
                                                                              SETVAR $SWITCHBOARD~$5 $SWITCHBOARD~$6
                                                                              OR $SWITCHBOARD~$5 $SWITCHBOARD~$9
                                                                              SETVAR $SWITCHBOARD~$1 $SWITCHBOARD~$2
                                                                              OR $SWITCHBOARD~$1 $SWITCHBOARD~$5
                                                                              if $SWITCHBOARD~$1
                                                                                SETVAR $SWITCHBOARD~NEXT_LENGTH 60
                                                                                SETVAR $SWITCHBOARD~I 1
                                                                                SETVAR $SWITCHBOARD~LENGTH 1
:SWITCHBOARD~:94
                                                                                ISLESSEREQUAL $SWITCHBOARD~$1 $SWITCHBOARD~I $SWITCHBOARD~MESSAGE_LENGTH
                                                                                if $SWITCHBOARD~$1
                                                                                  CUTTEXT $SWITCHBOARD~MESSAGE $SWITCHBOARD~CHARACTER $SWITCHBOARD~I 1
                                                                                  ISEQUAL $SWITCHBOARD~$3 $SWITCHBOARD~CHARACTER " "
                                                                                  ISGREATEREQUAL $SWITCHBOARD~$6 $SWITCHBOARD~LENGTH $SWITCHBOARD~NEXT_LENGTH
                                                                                  SETVAR $SWITCHBOARD~$2 $SWITCHBOARD~$3
                                                                                  AND $SWITCHBOARD~$2 $SWITCHBOARD~$6
                                                                                  ISEQUAL $SWITCHBOARD~$10 $SWITCHBOARD~CHARACTER "*"
                                                                                  ISGREATER $SWITCHBOARD~$13 $SWITCHBOARD~LENGTH 1
                                                                                  SETVAR $SWITCHBOARD~$9 $SWITCHBOARD~$10
                                                                                  AND $SWITCHBOARD~$9 $SWITCHBOARD~$13
                                                                                  SETVAR $SWITCHBOARD~$1 $SWITCHBOARD~$2
                                                                                  OR $SWITCHBOARD~$1 $SWITCHBOARD~$9
                                                                                  if $SWITCHBOARD~$1
                                                                                    ISLESSER $SWITCHBOARD~$1 $SWITCHBOARD~I $SWITCHBOARD~MESSAGE_LENGTH
                                                                                    if $SWITCHBOARD~$1
                                                                                      SETVAR $SWITCHBOARD~$1 $SWITCHBOARD~I
                                                                                      SUBTRACT $SWITCHBOARD~$1 1
                                                                                      CUTTEXT $SWITCHBOARD~MESSAGE $SWITCHBOARD~FIRST_HALF 1 $SWITCHBOARD~$1
                                                                                      SETVAR $SWITCHBOARD~$1 $SWITCHBOARD~I
                                                                                      ADD $SWITCHBOARD~$1 1
                                                                                      CUTTEXT $SWITCHBOARD~MESSAGE $SWITCHBOARD~SECOND_HALF $SWITCHBOARD~$1 999999999
                                                                                      if $SWITCHBOARD~NODISCORD
                                                                                        MERGETEXT "*" $SWITCHBOARD~DISCORD_IGNORE $SWITCHBOARD~$3
                                                                                        MERGETEXT $SWITCHBOARD~FIRST_HALF $SWITCHBOARD~$3 $SWITCHBOARD~$1
                                                                                        SETVAR $SWITCHBOARD~FIRST_HALF $SWITCHBOARD~$1
                                                                                        ADD $SWITCHBOARD~I $SWITCHBOARD~DISCORD_IGNORE_LENGTH
                                                                                        ADD $SWITCHBOARD~MESSAGE_LENGTH $SWITCHBOARD~DISCORD_IGNORE_LENGTH
:SWITCHBOARD~:100
                                                                                        MERGETEXT $SWITCHBOARD~FIRST_HALF "* " $SWITCHBOARD~$1
                                                                                        SETVAR $SWITCHBOARD~FIRST_HALF $SWITCHBOARD~$1
                                                                                        ADD $SWITCHBOARD~I 1
                                                                                        ADD $SWITCHBOARD~MESSAGE_LENGTH 1
:SWITCHBOARD~:100
:SWITCHBOARD~:101
                                                                                        MERGETEXT $SWITCHBOARD~FIRST_HALF $SWITCHBOARD~SECOND_HALF $SWITCHBOARD~$1
                                                                                        SETVAR $SWITCHBOARD~MESSAGE $SWITCHBOARD~$1
                                                                                        SETVAR $SWITCHBOARD~LENGTH 0
:SWITCHBOARD~:98
:SWITCHBOARD~:99
:SWITCHBOARD~:96
:SWITCHBOARD~:97
                                                                                        ADD $SWITCHBOARD~LENGTH 1
                                                                                        ADD $SWITCHBOARD~I 1
:SWITCHBOARD~:95
:SWITCHBOARD~:92
:SWITCHBOARD~:93
:SWITCHBOARD~:90
:SWITCHBOARD~:91
                                                                                        SETVAR $SWITCHBOARD~NEW_MESSAGE $SWITCHBOARD~MESSAGE
                                                                                        RETURN
:COMBAT~INIT
                                                                                        SETARRAY $PLAYER~TRADERS 50
                                                                                        SETARRAY $PLAYER~FAKETRADERS 50
                                                                                        SETARRAY $PLAYER~EMPTYSHIPS 100
                                                                                        SETVAR $PLAYER~RANKSLENGTH 46
                                                                                        SETARRAY $PLAYER~RANKS $PLAYER~RANKSLENGTH
                                                                                        SETVAR $PLAYER~RANKS[1] "36mCivilian"
                                                                                        SETVAR $PLAYER~RANKS[2] "36mPrivate 1st Class"
                                                                                        SETVAR $PLAYER~RANKS[3] "36mPrivate"
                                                                                        SETVAR $PLAYER~RANKS[4] "36mLance Corporal"
                                                                                        SETVAR $PLAYER~RANKS[5] "36mCorporal"
                                                                                        SETVAR $PLAYER~RANKS[6] "36mStaff Sergeant"
                                                                                        SETVAR $PLAYER~RANKS[7] "36mGunnery Sergeant"
                                                                                        SETVAR $PLAYER~RANKS[8] "36m1st Sergeant"
                                                                                        SETVAR $PLAYER~RANKS[9] "36mSergeant Major"
                                                                                        SETVAR $PLAYER~RANKS[10] "36mSergeant"
                                                                                        SETVAR $PLAYER~RANKS[11] "31mAnnoyance"
                                                                                        SETVAR $PLAYER~RANKS[12] "31mNuisance 3rd Class"
                                                                                        SETVAR $PLAYER~RANKS[13] "31mNuisance 2nd Class"
                                                                                        SETVAR $PLAYER~RANKS[14] "31mNuisance 1st Class"
                                                                                        SETVAR $PLAYER~RANKS[15] "31mMenace 3rd Class"
                                                                                        SETVAR $PLAYER~RANKS[16] "31mMenace 2nd Class"
                                                                                        SETVAR $PLAYER~RANKS[17] "31mMenace 1st Class"
                                                                                        SETVAR $PLAYER~RANKS[18] "31mSmuggler 3rd Class"
                                                                                        SETVAR $PLAYER~RANKS[19] "31mSmuggler 2nd Class"
                                                                                        SETVAR $PLAYER~RANKS[20] "31mSmuggler 1st Class"
                                                                                        SETVAR $PLAYER~RANKS[21] "31mSmuggler Savant"
                                                                                        SETVAR $PLAYER~RANKS[22] "31mRobber"
                                                                                        SETVAR $PLAYER~RANKS[23] "31mTerrorist"
                                                                                        SETVAR $PLAYER~RANKS[24] "31mInfamous Pirate"
                                                                                        SETVAR $PLAYER~RANKS[25] "31mNotorious Pirate"
                                                                                        SETVAR $PLAYER~RANKS[26] "31mDread Pirate"
                                                                                        SETVAR $PLAYER~RANKS[27] "31mPirate"
                                                                                        SETVAR $PLAYER~RANKS[28] "31mGalactic Scourge"
                                                                                        SETVAR $PLAYER~RANKS[29] "31mEnemy of the State"
                                                                                        SETVAR $PLAYER~RANKS[30] "31mEnemy of the People"
                                                                                        SETVAR $PLAYER~RANKS[31] "31mEnemy of Humankind"
                                                                                        SETVAR $PLAYER~RANKS[32] "31mHeinous Overlord"
                                                                                        SETVAR $PLAYER~RANKS[33] "31mPrime Evil"
                                                                                        SETVAR $PLAYER~RANKS[34] "36mChief Warrant Officer"
                                                                                        SETVAR $PLAYER~RANKS[35] "36mWarrant Officer"
                                                                                        SETVAR $PLAYER~RANKS[36] "36mEnsign"
                                                                                        SETVAR $PLAYER~RANKS[37] "36mLieutenant J.G."
                                                                                        SETVAR $PLAYER~RANKS[38] "36mLieutenant Commander"
                                                                                        SETVAR $PLAYER~RANKS[39] "36mLieutenant"
                                                                                        SETVAR $PLAYER~RANKS[40] "36mCommander"
                                                                                        SETVAR $PLAYER~RANKS[41] "36mCaptain"
                                                                                        SETVAR $PLAYER~RANKS[42] "36mCommodore"
                                                                                        SETVAR $PLAYER~RANKS[43] "36mRear Admiral"
                                                                                        SETVAR $PLAYER~RANKS[44] "36mVice Admiral"
                                                                                        SETVAR $PLAYER~RANKS[45] "36mFleet Admiral"
                                                                                        SETVAR $PLAYER~RANKS[46] "36mAdmiral"
                                                                                        SETVAR $PLAYER~LASTTARGET ""
                                                                                        RETURN
:SHIP~LOADSHIPINFO
                                                                                        SETVAR $SHIP~SHIPCOUNTER 1
:SHIP~COUNT_THE_SHIPS
                                                                                        LOADVAR $SHIP~CAP_FILE
                                                                                        FILEEXISTS $SHIP~EXISTS $SHIP~CAP_FILE
                                                                                        if $SHIP~EXISTS
                                                                                          READ $SHIP~CAP_FILE $SHIP~SHIPINF $SHIP~SHIPCOUNTER
                                                                                          ISNOTEQUAL $SHIP~$1 $SHIP~SHIPINF "EOF"
                                                                                          if $SHIP~$1
                                                                                            ADD $SHIP~SHIPCOUNTER 1
                                                                                            goto :COUNT_THE_SHIPS
:SHIP~:104
:SHIP~:105
                                                                                            SETARRAY $SHIP~SHIPLIST $SHIP~SHIPCOUNTER 9
                                                                                            SETVAR $SHIP~SHIPCOUNTER 1
:SHIP~READSHIPLIST
                                                                                            READ $SHIP~CAP_FILE $SHIP~SHIPINF $SHIP~SHIPCOUNTER
                                                                                            ISNOTEQUAL $SHIP~$1 $SHIP~SHIPINF "EOF"
                                                                                            if $SHIP~$1
                                                                                              GOSUB :PROCESS_SHIP_LINE
                                                                                              MERGETEXT " " $SHIP~DEFODD $SHIP~$3
                                                                                              MERGETEXT $SHIP~SHIELDS $SHIP~$3 $SHIP~$1
                                                                                              SETVAR $SHIP~SHIP[$SHIP~SHIPNAME] $SHIP~$1
                                                                                              SETVAR $SHIP~SHIPLIST[$SHIP~SHIPCOUNTER] $SHIP~SHIPNAME
                                                                                              SETVAR $SHIP~SHIPLIST[$SHIP~SHIPCOUNTER][1] $SHIP~SHIELDS
                                                                                              SETVAR $SHIP~SHIPLIST[$SHIP~SHIPCOUNTER][2] $SHIP~DEFODD
                                                                                              SETVAR $SHIP~SHIPLIST[$SHIP~SHIPCOUNTER][3] $SHIP~OFF_ODDS
                                                                                              SETVAR $SHIP~SHIPLIST[$SHIP~SHIPCOUNTER][4] $SHIP~MAX_HOLDS
                                                                                              SETVAR $SHIP~SHIPLIST[$SHIP~SHIPCOUNTER][5] $SHIP~MAX_FIGHTERS
                                                                                              SETVAR $SHIP~SHIPLIST[$SHIP~SHIPCOUNTER][6] $SHIP~INIT_HOLDS
                                                                                              SETVAR $SHIP~SHIPLIST[$SHIP~SHIPCOUNTER][7] $SHIP~TPW
                                                                                              SETVAR $SHIP~SHIPLIST[$SHIP~SHIPCOUNTER][8] $SHIP~ISDEFENDER
                                                                                              SETVAR $SHIP~SHIPLIST[$SHIP~SHIPCOUNTER][9] $SHIP~SHIP_COST
                                                                                              ADD $SHIP~SHIPCOUNTER 1
                                                                                              goto :READSHIPLIST
:SHIP~:106
:SHIP~:107
                                                                                              SETVAR $SHIP~SHIPSTATS TRUE
:SHIP~:102
:SHIP~:103
                                                                                              RETURN
:SHIP~PROCESS_SHIP_LINE
                                                                                              GETWORD $SHIP~SHIPINF $SHIP~SHIELDS 1
                                                                                              GETLENGTH $SHIP~SHIELDS $SHIP~SHIELDLEN
                                                                                              GETWORD $SHIP~SHIPINF $SHIP~DEFODD 2
                                                                                              GETLENGTH $SHIP~DEFODD $SHIP~DEFODDLEN
                                                                                              GETWORD $SHIP~SHIPINF $SHIP~OFF_ODDS 3
                                                                                              GETLENGTH $SHIP~OFF_ODDS $SHIP~FILLER1LEN
                                                                                              GETWORD $SHIP~SHIPINF $SHIP~SHIP_COST 4
                                                                                              GETLENGTH $SHIP~SHIP_COST $SHIP~FILLER2LEN
                                                                                              GETWORD $SHIP~SHIPINF $SHIP~MAX_HOLDS 5
                                                                                              GETLENGTH $SHIP~MAX_HOLDS $SHIP~FILLER3LEN
                                                                                              GETWORD $SHIP~SHIPINF $SHIP~MAX_FIGHTERS 6
                                                                                              GETLENGTH $SHIP~MAX_FIGHTERS $SHIP~FILLER4LEN
                                                                                              GETWORD $SHIP~SHIPINF $SHIP~INIT_HOLDS 7
                                                                                              GETLENGTH $SHIP~INIT_HOLDS $SHIP~FILLER5LEN
                                                                                              GETWORD $SHIP~SHIPINF $SHIP~TPW 8
                                                                                              GETLENGTH $SHIP~TPW $SHIP~FILLER6LEN
                                                                                              GETWORD $SHIP~SHIPINF $SHIP~ISDEFENDER 9
                                                                                              GETLENGTH $SHIP~ISDEFENDER $SHIP~FILLER7LEN
                                                                                              SETVAR $SHIP~STARTLEN ($SHIP~SHIELDLEN + $SHIP~DEFODDLEN + $SHIP~FILLER1LEN + $SHIP~FILLER2LEN + $SHIP~FILLER3LEN + $SHIP~FILLER4LEN + $SHIP~FILLER5LEN + $SHIP~FILLER6LEN + $SHIP~FILLER7LEN + 10)
                                                                                              CUTTEXT $SHIP~SHIPINF $SHIP~SHIPNAME $SHIP~STARTLEN 999
                                                                                              RETURN
:SHIP~GETSHIPCAPSTATS
                                                                                              SEND "cn"
                                                                                              SETTEXTTRIGGER WAITON1 :WAITON1 "(2) Animation display"
                                                                                              PAUSE
:SHIP~WAITON1
                                                                                              GETWORD CURRENTLINE $SHIP~ANSI_ONOFF 5
                                                                                              ISEQUAL $SHIP~$1 $SHIP~ANSI_ONOFF "On"
                                                                                              if $SHIP~$1
                                                                                                SEND "2qq"
:SHIP~:108
                                                                                                SEND "qq"
:SHIP~:108
:SHIP~:109
                                                                                                SETARRAY $SHIP~ALPHA 20
                                                                                                DELETE $SHIP~CAP_FILE
                                                                                                SETVAR $SHIP~ALPHA[1] "A"
                                                                                                SETVAR $SHIP~ALPHA[2] "B"
                                                                                                SETVAR $SHIP~ALPHA[3] "C"
                                                                                                SETVAR $SHIP~ALPHA[4] "D"
                                                                                                SETVAR $SHIP~ALPHA[5] "E"
                                                                                                SETVAR $SHIP~ALPHA[6] "F"
                                                                                                SETVAR $SHIP~ALPHA[7] "G"
                                                                                                SETVAR $SHIP~ALPHA[8] "H"
                                                                                                SETVAR $SHIP~ALPHA[9] "I"
                                                                                                SETVAR $SHIP~ALPHA[10] "J"
                                                                                                SETVAR $SHIP~ALPHA[11] "K"
                                                                                                SETVAR $SHIP~ALPHA[12] "L"
                                                                                                SETVAR $SHIP~ALPHA[13] "M"
                                                                                                SETVAR $SHIP~ALPHA[14] "N"
                                                                                                SETVAR $SHIP~ALPHA[15] "O"
                                                                                                SETVAR $SHIP~ALPHA[16] "P"
                                                                                                SETVAR $SHIP~ALPHA[17] "R"
                                                                                                SETVAR $SHIP~ALPHALOOP 0
                                                                                                SETVAR $SHIP~TOTALSHIPS 0
                                                                                                SETVAR $SHIP~FIRSTSHIPNAME ""
                                                                                                SETVAR $SHIP~NEXTPAGE 1
                                                                                                SEND "CC@?"
                                                                                                SETTEXTTRIGGER WAITON2 :WAITON2 "Average Interval Lag"
                                                                                                PAUSE
:SHIP~WAITON2
:SHIP~SHP_LOOP
                                                                                                SETTEXTLINETRIGGER GRAB_SHIP :SHP_SHIPNAMES "> "
                                                                                                PAUSE
:SHIP~SHP_SHIPNAMES
                                                                                                ISEQUAL $SHIP~$1 CURRENTLINE ""
                                                                                                if $SHIP~$1
                                                                                                  goto :SHP_LOOP
:SHIP~:110
:SHIP~:111
                                                                                                  GETWORD CURRENTLINE $SHIP~STOPPER 1
                                                                                                  ISEQUAL $SHIP~$1 $SHIP~STOPPER "<+>"
                                                                                                  if $SHIP~$1
                                                                                                    SEND "+"
                                                                                                    SETTEXTTRIGGER WAITON3 :WAITON3 "(?=List) ?"
                                                                                                    PAUSE
:SHIP~WAITON3
                                                                                                    SETVAR $SHIP~NEXTPAGE 1
                                                                                                    goto :SHP_LOOP
:SHIP~:112
                                                                                                    ISEQUAL $SHIP~$1 $SHIP~STOPPER "<Q>"
                                                                                                    if $SHIP~$1
                                                                                                      goto :SHP_GETSHIPSTATS
:SHIP~:114
:SHIP~:113
                                                                                                      ISEQUAL $SHIP~$1 $SHIP~NEXTPAGE 1
                                                                                                      if $SHIP~$1
                                                                                                        SETVAR $SHIP~SHIPNAME CURRENTLINE
                                                                                                        STRIPTEXT $SHIP~SHIPNAME "<A> "
                                                                                                        ISEQUAL $SHIP~$1 $SHIP~SHIPNAME $SHIP~FIRSTSHIPNAME
                                                                                                        if $SHIP~$1
                                                                                                          goto :SHP_GETSHIPSTATS
:SHIP~:117
:SHIP~:118
                                                                                                          SETVAR $SHIP~NEXTPAGE 0
:SHIP~:115
:SHIP~:116
                                                                                                          ADD $SHIP~TOTALSHIPS 1
                                                                                                          ISEQUAL $SHIP~$1 $SHIP~TOTALSHIPS 1
                                                                                                          if $SHIP~$1
                                                                                                            SETVAR $SHIP~FIRSTSHIPNAME CURRENTLINE
                                                                                                            STRIPTEXT $SHIP~FIRSTSHIPNAME "<A> "
:SHIP~:119
:SHIP~:120
                                                                                                            goto :SHP_LOOP
:SHIP~SHP_GETSHIPSTATS
                                                                                                            SETVAR $SHIP~SHIPSTATLOOP 0
:SHIP~SHP_SHIPSTATS
:SHIP~:121
                                                                                                            ISLESSER $SHIP~$1 $SHIP~SHIPSTATLOOP $SHIP~TOTALSHIPS
                                                                                                            if $SHIP~$1
                                                                                                              ADD $SHIP~SHIPSTATLOOP 1
                                                                                                              ADD $SHIP~ALPHALOOP 1
                                                                                                              ISGREATER $SHIP~$1 $SHIP~ALPHALOOP 17
                                                                                                              if $SHIP~$1
                                                                                                                SEND "+"
                                                                                                                SETVAR $SHIP~ALPHALOOP 1
:SHIP~:123
:SHIP~:124
                                                                                                                SEND $SHIP~ALPHA[$SHIP~ALPHALOOP]
                                                                                                                SETTEXTLINETRIGGER SN :SN "Ship Class :"
                                                                                                                PAUSE
:SHIP~SN
                                                                                                                SETVAR $SHIP~LINE CURRENTLINE
                                                                                                                GETWORDPOS $SHIP~LINE $SHIP~POS :
                                                                                                                ADD $SHIP~POS 2
                                                                                                                CUTTEXT $SHIP~LINE $SHIP~SHIP_NAME $SHIP~POS 999
                                                                                                                SETTEXTLINETRIGGER HC :HC "Basic Hold Cost:"
                                                                                                                PAUSE
:SHIP~HC
                                                                                                                SETVAR $SHIP~LINE CURRENTLINE
                                                                                                                STRIPTEXT $SHIP~LINE "Basic Hold Cost:"
                                                                                                                STRIPTEXT $SHIP~LINE "Initial Holds:"
                                                                                                                STRIPTEXT $SHIP~LINE "Maximum Shields:"
                                                                                                                GETWORD $SHIP~LINE $SHIP~INIT_HOLDS 2
                                                                                                                GETWORD $SHIP~LINE $SHIP~MAX_SHIELDS 3
                                                                                                                STRIPTEXT $SHIP~MAX_SHIELDS ","
                                                                                                                SETTEXTLINETRIGGER OO :OO2 "Offensive Odds:"
                                                                                                                PAUSE
:SHIP~OO2
                                                                                                                SETVAR $SHIP~LINE CURRENTLINE
                                                                                                                STRIPTEXT $SHIP~LINE "Main Drive Cost:"
                                                                                                                STRIPTEXT $SHIP~LINE "Max Fighters:"
                                                                                                                STRIPTEXT $SHIP~LINE "Offensive Odds:"
                                                                                                                GETWORD $SHIP~LINE $SHIP~MAX_FIGS 2
                                                                                                                GETWORD $SHIP~LINE $SHIP~OFF_ODDS 3
                                                                                                                STRIPTEXT $SHIP~MAX_FIGS ","
                                                                                                                STRIPTEXT $SHIP~OFF_ODDS :1
                                                                                                                STRIPTEXT $SHIP~OFF_ODDS "."
                                                                                                                SETTEXTLINETRIGGER DO :DO "Defensive Odds:"
                                                                                                                PAUSE
:SHIP~DO
                                                                                                                SETVAR $SHIP~LINE CURRENTLINE
                                                                                                                STRIPTEXT $SHIP~LINE "Computer Cost:"
                                                                                                                STRIPTEXT $SHIP~LINE "Turns Per Warp:"
                                                                                                                STRIPTEXT $SHIP~LINE "Defensive Odds:"
                                                                                                                GETWORD $SHIP~LINE $SHIP~DEF_ODDS 3
                                                                                                                STRIPTEXT $SHIP~DEF_ODDS :1
                                                                                                                STRIPTEXT $SHIP~DEF_ODDS "."
                                                                                                                GETWORD $SHIP~LINE $SHIP~TPW 2
                                                                                                                SETTEXTLINETRIGGER SC :SC "Ship Base Cost:"
                                                                                                                PAUSE
:SHIP~SC
                                                                                                                SETVAR $SHIP~LINE CURRENTLINE
                                                                                                                STRIPTEXT $SHIP~LINE "Ship Base Cost:"
                                                                                                                GETWORD $SHIP~LINE $SHIP~COST 1
                                                                                                                STRIPTEXT $SHIP~COST ","
                                                                                                                GETLENGTH $SHIP~COST $SHIP~COSTLEN
                                                                                                                ISEQUAL $SHIP~$1 $SHIP~COSTLEN 7
                                                                                                                if $SHIP~$1
                                                                                                                  ADD $SHIP~COST 10000000
:SHIP~:125
:SHIP~:126
                                                                                                                  SETTEXTLINETRIGGER MH :MH "Maximum Holds:"
                                                                                                                  PAUSE
:SHIP~MH
                                                                                                                  SETVAR $SHIP~LINE CURRENTLINE
                                                                                                                  STRIPTEXT $SHIP~LINE "Maximum Holds:"
                                                                                                                  GETWORD $SHIP~LINE $SHIP~MAX_HOLDS 1
                                                                                                                  SETVAR $SHIP~ISDEFENDER FALSE
                                                                                                                  MERGETEXT " " $SHIP~SHIP_NAME $SHIP~$35
                                                                                                                  MERGETEXT $SHIP~ISDEFENDER $SHIP~$35 $SHIP~$33
                                                                                                                  MERGETEXT " " $SHIP~$33 $SHIP~$31
                                                                                                                  MERGETEXT $SHIP~TPW $SHIP~$31 $SHIP~$29
                                                                                                                  MERGETEXT " " $SHIP~$29 $SHIP~$27
                                                                                                                  MERGETEXT $SHIP~INIT_HOLDS $SHIP~$27 $SHIP~$25
                                                                                                                  MERGETEXT " " $SHIP~$25 $SHIP~$23
                                                                                                                  MERGETEXT $SHIP~MAX_FIGS $SHIP~$23 $SHIP~$21
                                                                                                                  MERGETEXT " " $SHIP~$21 $SHIP~$19
                                                                                                                  MERGETEXT $SHIP~MAX_HOLDS $SHIP~$19 $SHIP~$17
                                                                                                                  MERGETEXT " " $SHIP~$17 $SHIP~$15
                                                                                                                  MERGETEXT $SHIP~COST $SHIP~$15 $SHIP~$13
                                                                                                                  MERGETEXT " " $SHIP~$13 $SHIP~$11
                                                                                                                  MERGETEXT $SHIP~OFF_ODDS $SHIP~$11 $SHIP~$9
                                                                                                                  MERGETEXT " " $SHIP~$9 $SHIP~$7
                                                                                                                  MERGETEXT $SHIP~DEF_ODDS $SHIP~$7 $SHIP~$5
                                                                                                                  MERGETEXT " " $SHIP~$5 $SHIP~$3
                                                                                                                  MERGETEXT $SHIP~MAX_SHIELDS $SHIP~$3 $SHIP~$1
                                                                                                                  WRITE $SHIP~CAP_FILE $SHIP~$1
:SHIP~:122
                                                                                                                  SEND "qq"
                                                                                                                  RETURN
:PLAYER~QUIKSTATS
                                                                                                                  SETVAR $PLAYER~CURRENT_PROMPT "Undefined"
                                                                                                                  KILLTRIGGER NOPROMPT
                                                                                                                  KILLTRIGGER PROMPT
                                                                                                                  KILLTRIGGER STATLINETRIG
                                                                                                                  KILLTRIGGER GETLINE2
                                                                                                                  SETVAR $PLAYER~FEDSPACE FALSE
                                                                                                                  LOADVAR $PLAYER~UNLIMITEDGAME
                                                                                                                  MERGETEXT #145 #8 $PLAYER~$1
                                                                                                                  SETTEXTLINETRIGGER PROMPT :ALLPROMPTS $PLAYER~$1
                                                                                                                  SETTEXTLINETRIGGER STATLINETRIG :STATSTART #179
                                                                                                                  MERGETEXT #145 "/" $PLAYER~$1
                                                                                                                  SEND $PLAYER~$1
                                                                                                                  PAUSE
:PLAYER~ALLPROMPTS
                                                                                                                  GETWORD CURRENTLINE $PLAYER~CURRENT_PROMPT 1
                                                                                                                  SETVAR $PLAYER~FULL_CURRENT_PROMPT CURRENTLINE
                                                                                                                  STRIPTEXT $PLAYER~FULL_CURRENT_PROMPT #145
                                                                                                                  STRIPTEXT $PLAYER~FULL_CURRENT_PROMPT #8
                                                                                                                  STRIPTEXT $PLAYER~CURRENT_PROMPT #145
                                                                                                                  STRIPTEXT $PLAYER~CURRENT_PROMPT #8
                                                                                                                  MERGETEXT #145 #8 $PLAYER~$1
                                                                                                                  SETTEXTLINETRIGGER PROMPT :ALLPROMPTS $PLAYER~$1
                                                                                                                  PAUSE
:PLAYER~STATSTART
                                                                                                                  KILLTRIGGER PROMPT
                                                                                                                  SETVAR $PLAYER~STATS ""
                                                                                                                  SETVAR $PLAYER~WORDY ""
:PLAYER~STATSLINE
                                                                                                                  KILLTRIGGER STATLINETRIG
                                                                                                                  KILLTRIGGER GETLINE2
                                                                                                                  SETVAR $PLAYER~LINE2 CURRENTLINE
                                                                                                                  REPLACETEXT $PLAYER~LINE2 #179 " "
                                                                                                                  STRIPTEXT $PLAYER~LINE2 ","
                                                                                                                  MERGETEXT $PLAYER~STATS $PLAYER~LINE2 $PLAYER~$1
                                                                                                                  SETVAR $PLAYER~STATS $PLAYER~$1
                                                                                                                  GETWORDPOS $PLAYER~LINE2 $PLAYER~POS "Ship"
                                                                                                                  ISGREATER $PLAYER~$1 $PLAYER~POS 0
                                                                                                                  if $PLAYER~$1
                                                                                                                    goto :GOTSTATS
:PLAYER~:127
                                                                                                                    SETTEXTLINETRIGGER GETLINE2 :STATSLINE
                                                                                                                    PAUSE
:PLAYER~:127
:PLAYER~:128
:PLAYER~GOTSTATS
                                                                                                                    MERGETEXT $PLAYER~STATS " @@@" $PLAYER~$1
                                                                                                                    SETVAR $PLAYER~STATS $PLAYER~$1
                                                                                                                    SETVAR $PLAYER~CURRENT_WORD 0
:PLAYER~:129
                                                                                                                    ISNOTEQUAL $PLAYER~$1 $PLAYER~WORDY "@@@"
                                                                                                                    if $PLAYER~$1
                                                                                                                      ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Sect"
                                                                                                                      if $PLAYER~$1
                                                                                                                        SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                        ADD $PLAYER~$1 1
                                                                                                                        GETWORD $PLAYER~STATS $PLAYER~CURRENT_SECTOR $PLAYER~$1
                                                                                                                        ISLESSEREQUAL $PLAYER~$2 $PLAYER~CURRENT_SECTOR 10
                                                                                                                        ISEQUAL $PLAYER~$6 $PLAYER~CURRENT_SECTOR STARDOCK
                                                                                                                        ISEQUAL $PLAYER~$9 $PLAYER~CURRENT_SECTOR $MAP~STARDOCK
                                                                                                                        SETVAR $PLAYER~$5 $PLAYER~$6
                                                                                                                        OR $PLAYER~$5 $PLAYER~$9
                                                                                                                        SETVAR $PLAYER~$1 $PLAYER~$2
                                                                                                                        OR $PLAYER~$1 $PLAYER~$5
                                                                                                                        if $PLAYER~$1
                                                                                                                          SETVAR $PLAYER~FEDSPACE TRUE
:PLAYER~:133
:PLAYER~:134
:PLAYER~:131
                                                                                                                          ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Turns"
                                                                                                                          if $PLAYER~$1
                                                                                                                            SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                            ADD $PLAYER~$1 1
                                                                                                                            GETWORD $PLAYER~STATS $PLAYER~TURNS $PLAYER~$1
                                                                                                                            ISEQUAL $PLAYER~$1 $PLAYER~UNLIMITEDGAME TRUE
                                                                                                                            if $PLAYER~$1
                                                                                                                              SETVAR $PLAYER~TURNS 65000
:PLAYER~:136
:PLAYER~:137
:PLAYER~:135
                                                                                                                              ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Creds"
                                                                                                                              if $PLAYER~$1
                                                                                                                                SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                ADD $PLAYER~$1 1
                                                                                                                                GETWORD $PLAYER~STATS $PLAYER~CREDITS $PLAYER~$1
:PLAYER~:138
                                                                                                                                ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Figs"
                                                                                                                                if $PLAYER~$1
                                                                                                                                  SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                  ADD $PLAYER~$1 1
                                                                                                                                  GETWORD $PLAYER~STATS $PLAYER~FIGHTERS $PLAYER~$1
:PLAYER~:139
                                                                                                                                  ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Shlds"
                                                                                                                                  if $PLAYER~$1
                                                                                                                                    SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                    ADD $PLAYER~$1 1
                                                                                                                                    GETWORD $PLAYER~STATS $PLAYER~SHIELDS $PLAYER~$1
:PLAYER~:140
                                                                                                                                    ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Hlds"
                                                                                                                                    if $PLAYER~$1
                                                                                                                                      SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                      ADD $PLAYER~$1 1
                                                                                                                                      GETWORD $PLAYER~STATS $PLAYER~TOTAL_HOLDS $PLAYER~$1
:PLAYER~:141
                                                                                                                                      ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Ore"
                                                                                                                                      if $PLAYER~$1
                                                                                                                                        SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                        ADD $PLAYER~$1 1
                                                                                                                                        GETWORD $PLAYER~STATS $PLAYER~ORE_HOLDS $PLAYER~$1
:PLAYER~:142
                                                                                                                                        ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Org"
                                                                                                                                        if $PLAYER~$1
                                                                                                                                          SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                          ADD $PLAYER~$1 1
                                                                                                                                          GETWORD $PLAYER~STATS $PLAYER~ORGANIC_HOLDS $PLAYER~$1
:PLAYER~:143
                                                                                                                                          ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Equ"
                                                                                                                                          if $PLAYER~$1
                                                                                                                                            SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                            ADD $PLAYER~$1 1
                                                                                                                                            GETWORD $PLAYER~STATS $PLAYER~EQUIPMENT_HOLDS $PLAYER~$1
:PLAYER~:144
                                                                                                                                            ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Col"
                                                                                                                                            if $PLAYER~$1
                                                                                                                                              SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                              ADD $PLAYER~$1 1
                                                                                                                                              GETWORD $PLAYER~STATS $PLAYER~COLONIST_HOLDS $PLAYER~$1
:PLAYER~:145
                                                                                                                                              ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Phot"
                                                                                                                                              if $PLAYER~$1
                                                                                                                                                SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                                ADD $PLAYER~$1 1
                                                                                                                                                GETWORD $PLAYER~STATS $PLAYER~PHOTONS $PLAYER~$1
:PLAYER~:146
                                                                                                                                                ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Armd"
                                                                                                                                                if $PLAYER~$1
                                                                                                                                                  SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                                  ADD $PLAYER~$1 1
                                                                                                                                                  GETWORD $PLAYER~STATS $PLAYER~ARMIDS $PLAYER~$1
:PLAYER~:147
                                                                                                                                                  ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Lmpt"
                                                                                                                                                  if $PLAYER~$1
                                                                                                                                                    SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                                    ADD $PLAYER~$1 1
                                                                                                                                                    GETWORD $PLAYER~STATS $PLAYER~LIMPETS $PLAYER~$1
:PLAYER~:148
                                                                                                                                                    ISEQUAL $PLAYER~$1 $PLAYER~WORDY "GTorp"
                                                                                                                                                    if $PLAYER~$1
                                                                                                                                                      SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                                      ADD $PLAYER~$1 1
                                                                                                                                                      GETWORD $PLAYER~STATS $PLAYER~GENESIS $PLAYER~$1
:PLAYER~:149
                                                                                                                                                      ISEQUAL $PLAYER~$1 $PLAYER~WORDY "TWarp"
                                                                                                                                                      if $PLAYER~$1
                                                                                                                                                        SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                                        ADD $PLAYER~$1 1
                                                                                                                                                        GETWORD $PLAYER~STATS $PLAYER~TWARP_TYPE $PLAYER~$1
:PLAYER~:150
                                                                                                                                                        ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Clks"
                                                                                                                                                        if $PLAYER~$1
                                                                                                                                                          SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                                          ADD $PLAYER~$1 1
                                                                                                                                                          GETWORD $PLAYER~STATS $PLAYER~CLOAKS $PLAYER~$1
:PLAYER~:151
                                                                                                                                                          ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Beacns"
                                                                                                                                                          if $PLAYER~$1
                                                                                                                                                            SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                                            ADD $PLAYER~$1 1
                                                                                                                                                            GETWORD $PLAYER~STATS $PLAYER~BEACONS $PLAYER~$1
:PLAYER~:152
                                                                                                                                                            ISEQUAL $PLAYER~$1 $PLAYER~WORDY "AtmDt"
                                                                                                                                                            if $PLAYER~$1
                                                                                                                                                              SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                                              ADD $PLAYER~$1 1
                                                                                                                                                              GETWORD $PLAYER~STATS $PLAYER~ATOMIC $PLAYER~$1
:PLAYER~:153
                                                                                                                                                              ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Corbo"
                                                                                                                                                              if $PLAYER~$1
                                                                                                                                                                SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                                                ADD $PLAYER~$1 1
                                                                                                                                                                GETWORD $PLAYER~STATS $PLAYER~CORBO $PLAYER~$1
:PLAYER~:154
                                                                                                                                                                ISEQUAL $PLAYER~$1 $PLAYER~WORDY "EPrb"
                                                                                                                                                                if $PLAYER~$1
                                                                                                                                                                  SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                                                  ADD $PLAYER~$1 1
                                                                                                                                                                  GETWORD $PLAYER~STATS $PLAYER~EPROBES $PLAYER~$1
:PLAYER~:155
                                                                                                                                                                  ISEQUAL $PLAYER~$1 $PLAYER~WORDY "MDis"
                                                                                                                                                                  if $PLAYER~$1
                                                                                                                                                                    SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                                                    ADD $PLAYER~$1 1
                                                                                                                                                                    GETWORD $PLAYER~STATS $PLAYER~MINE_DISRUPTORS $PLAYER~$1
:PLAYER~:156
                                                                                                                                                                    ISEQUAL $PLAYER~$1 $PLAYER~WORDY "PsPrb"
                                                                                                                                                                    if $PLAYER~$1
                                                                                                                                                                      SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                                                      ADD $PLAYER~$1 1
                                                                                                                                                                      GETWORD $PLAYER~STATS $PLAYER~PSYCHIC_PROBE $PLAYER~$1
:PLAYER~:157
                                                                                                                                                                      ISEQUAL $PLAYER~$1 $PLAYER~WORDY "PlScn"
                                                                                                                                                                      if $PLAYER~$1
                                                                                                                                                                        SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                                                        ADD $PLAYER~$1 1
                                                                                                                                                                        GETWORD $PLAYER~STATS $PLAYER~PLANET_SCANNER $PLAYER~$1
:PLAYER~:158
                                                                                                                                                                        ISEQUAL $PLAYER~$1 $PLAYER~WORDY "LRS"
                                                                                                                                                                        if $PLAYER~$1
                                                                                                                                                                          SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                                                          ADD $PLAYER~$1 1
                                                                                                                                                                          GETWORD $PLAYER~STATS $PLAYER~SCAN_TYPE $PLAYER~$1
:PLAYER~:159
                                                                                                                                                                          ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Aln"
                                                                                                                                                                          if $PLAYER~$1
                                                                                                                                                                            SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                                                            ADD $PLAYER~$1 1
                                                                                                                                                                            GETWORD $PLAYER~STATS $PLAYER~ALIGNMENT $PLAYER~$1
:PLAYER~:160
                                                                                                                                                                            ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Exp"
                                                                                                                                                                            if $PLAYER~$1
                                                                                                                                                                              SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                                                              ADD $PLAYER~$1 1
                                                                                                                                                                              GETWORD $PLAYER~STATS $PLAYER~EXPERIENCE $PLAYER~$1
:PLAYER~:161
                                                                                                                                                                              ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Corp"
                                                                                                                                                                              if $PLAYER~$1
                                                                                                                                                                                SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                                                                ADD $PLAYER~$1 1
                                                                                                                                                                                GETWORD $PLAYER~STATS $PLAYER~CORP $PLAYER~$1
                                                                                                                                                                                SETVAR $PLAYER~CORPNUMBER $PLAYER~CORP
                                                                                                                                                                                SAVEVAR $PLAYER~CORPNUMBER
:PLAYER~:162
                                                                                                                                                                                ISEQUAL $PLAYER~$1 $PLAYER~WORDY "Ship"
                                                                                                                                                                                if $PLAYER~$1
                                                                                                                                                                                  SETVAR $PLAYER~$1 $PLAYER~CURRENT_WORD
                                                                                                                                                                                  ADD $PLAYER~$1 1
                                                                                                                                                                                  GETWORD $PLAYER~STATS $PLAYER~SHIP_NUMBER $PLAYER~$1
:PLAYER~:163
:PLAYER~:132
                                                                                                                                                                                  ADD $PLAYER~CURRENT_WORD 1
                                                                                                                                                                                  GETWORD $PLAYER~STATS $PLAYER~WORDY $PLAYER~CURRENT_WORD
:PLAYER~:130
:PLAYER~DONEQUIKSTATS
                                                                                                                                                                                  KILLTRIGGER STATLINETRIG
                                                                                                                                                                                  KILLTRIGGER GETLINE2
                                                                                                                                                                                  SAVEVAR $PLAYER~UNLIMITEDGAME
                                                                                                                                                                                  if $PLAYER~SAVE
                                                                                                                                                                                    SAVEVAR $PLAYER~CORP
                                                                                                                                                                                    SAVEVAR $PLAYER~CREDITS
                                                                                                                                                                                    SAVEVAR $PLAYER~CURRENT_SECTOR
                                                                                                                                                                                    SAVEVAR $PLAYER~TURNS
                                                                                                                                                                                    SAVEVAR $PLAYER~FIGHTERS
                                                                                                                                                                                    SAVEVAR $PLAYER~SHIELDS
                                                                                                                                                                                    SAVEVAR $PLAYER~TOTAL_HOLDS
                                                                                                                                                                                    SAVEVAR $PLAYER~ORE_HOLDS
                                                                                                                                                                                    SAVEVAR $PLAYER~ORGANIC_HOLDS
                                                                                                                                                                                    SAVEVAR $PLAYER~EQUIPMENT_HOLDS
                                                                                                                                                                                    SAVEVAR $PLAYER~COLONIST_HOLDS
                                                                                                                                                                                    SAVEVAR $PLAYER~PHOTONS
                                                                                                                                                                                    SAVEVAR $PLAYER~ARMIDS
                                                                                                                                                                                    SAVEVAR $PLAYER~LIMPETS
                                                                                                                                                                                    SAVEVAR $PLAYER~GENESIS
                                                                                                                                                                                    SAVEVAR $PLAYER~TWARP_TYPE
                                                                                                                                                                                    SAVEVAR $PLAYER~CLOAKS
                                                                                                                                                                                    SAVEVAR $PLAYER~BEACONS
                                                                                                                                                                                    SAVEVAR $PLAYER~ATOMIC
                                                                                                                                                                                    SAVEVAR $PLAYER~CORBO
                                                                                                                                                                                    SAVEVAR $PLAYER~EPROBES
                                                                                                                                                                                    SAVEVAR $PLAYER~MINE_DISRUPTORS
                                                                                                                                                                                    SAVEVAR $PLAYER~PSYCHIC_PROBE
                                                                                                                                                                                    SAVEVAR $PLAYER~PLANET_SCANNER
                                                                                                                                                                                    SAVEVAR $PLAYER~SCAN_TYPE
                                                                                                                                                                                    SAVEVAR $PLAYER~ALIGNMENT
                                                                                                                                                                                    SAVEVAR $PLAYER~EXPERIENCE
                                                                                                                                                                                    SAVEVAR $PLAYER~SHIP_NUMBER
                                                                                                                                                                                    SAVEVAR $PLAYER~TRADER_NAME
:PLAYER~:164
:PLAYER~:165
                                                                                                                                                                                    RETURN
                                                                                                                                                                                    SETVAR $SHIP~SHIP_OFFENSIVE_ODDS 0
                                                                                                                                                                                    SETVAR $SHIP~SHIP_FIGHTERS_MAX 0
                                                                                                                                                                                    SETVAR $SHIP~SHIP_MAX_ATTACK 0
                                                                                                                                                                                    SETVAR $SHIP~SHIP_MINES_MAX 0
                                                                                                                                                                                    SETVAR $SHIP~SHIP_SHIELD_MAX 0
:SHIP~GETSHIPSTATS
                                                                                                                                                                                    SEND "c;q"
                                                                                                                                                                                    SETTEXTLINETRIGGER GETSHIPOFFENSE :SHIPOFFENSEODDS "Offensive Odds: "
                                                                                                                                                                                    SETTEXTLINETRIGGER GETSHIPFIGHTERS :SHIPMAXFIGSPERATTACK " TransWarp Drive:   "
                                                                                                                                                                                    SETTEXTLINETRIGGER GETSHIPMINES :SHIPMAXMINES " Mine Max:  "
                                                                                                                                                                                    SETTEXTLINETRIGGER GETSHIPGENESIS :SHIPMAXGENESIS " Genesis Max:  "
                                                                                                                                                                                    SETTEXTLINETRIGGER GETSHIPSHIELDS :SHIPMAXSHIELDS "Maximum Shields:"
                                                                                                                                                                                    PAUSE
:SHIP~SHIPMAXSHIELDS
                                                                                                                                                                                    SETVAR $SHIP~SHIELD_LINE CURRENTLINE
                                                                                                                                                                                    REPLACETEXT $SHIP~SHIELD_LINE : "  "
                                                                                                                                                                                    REPLACETEXT $SHIP~SHIELD_LINE "," ""
                                                                                                                                                                                    GETWORD $SHIP~SHIELD_LINE $SHIP~SHIP_SHIELD_MAX 10
                                                                                                                                                                                    PAUSE
:SHIP~SHIPOFFENSEODDS
                                                                                                                                                                                    GETWORDPOS CURRENTANSILINE $SHIP~POS "[0;31m:[1;36m1"
                                                                                                                                                                                    ISGREATER $SHIP~$1 $SHIP~POS 0
                                                                                                                                                                                    if $SHIP~$1
                                                                                                                                                                                      GETTEXT CURRENTANSILINE $SHIP~SHIP_OFFENSIVE_ODDS "Offensive Odds[1;33m:[36m " "[0;31m:[1;36m1"
                                                                                                                                                                                      STRIPTEXT $SHIP~SHIP_OFFENSIVE_ODDS "."
                                                                                                                                                                                      STRIPTEXT $SHIP~SHIP_OFFENSIVE_ODDS " "
                                                                                                                                                                                      GETTEXT CURRENTANSILINE $SHIP~SHIP_FIGHTERS_MAX "Max Fighters[1;33m:[36m" "[0;32m Offensive Odds"
                                                                                                                                                                                      STRIPTEXT $SHIP~SHIP_FIGHTERS_MAX ","
                                                                                                                                                                                      STRIPTEXT $SHIP~SHIP_FIGHTERS_MAX " "
:SHIP~:166
:SHIP~:167
                                                                                                                                                                                      PAUSE
:SHIP~SHIPMAXMINES
                                                                                                                                                                                      GETTEXT CURRENTLINE $SHIP~SHIP_MINES_MAX "Mine Max:" "Beacon Max:"
                                                                                                                                                                                      STRIPTEXT $SHIP~SHIP_MINES_MAX " "
                                                                                                                                                                                      PAUSE
:SHIP~SHIPMAXGENESIS
                                                                                                                                                                                      GETTEXT CURRENTLINE $SHIP~SHIP_GENESIS_MAX "Genesis Max:" "Long Range Scan:"
                                                                                                                                                                                      STRIPTEXT $SHIP~SHIP_GENESIS_MAX " "
                                                                                                                                                                                      PAUSE
:SHIP~SHIPMAXFIGSPERATTACK
                                                                                                                                                                                      GETWORDPOS CURRENTANSILINE $SHIP~POS "[0m[32m Max Figs Per Attack[1;33m:[36m"
                                                                                                                                                                                      ISGREATER $SHIP~$1 $SHIP~POS 0
                                                                                                                                                                                      if $SHIP~$1
                                                                                                                                                                                        GETTEXT CURRENTANSILINE $SHIP~SHIP_MAX_ATTACK "[0m[32m Max Figs Per Attack[1;33m:[36m" "[0;32mTransWarp"
                                                                                                                                                                                        STRIPTEXT $SHIP~SHIP_MAX_ATTACK " "
:SHIP~:168
:SHIP~:169
                                                                                                                                                                                        RETURN
:SECTOR~GETSECTORDATA
                                                                                                                                                                                        SETVAR $SECTOR~ENDLINE "_ENDLINE_"
                                                                                                                                                                                        SETVAR $SECTOR~STARTLINE "_STARTLINE_"
                                                                                                                                                                                        KILLALLTRIGGERS
                                                                                                                                                                                        ISEQUAL $SECTOR~$1 $PLAYER~STARTINGLOCATION "Citadel"
                                                                                                                                                                                        if $SECTOR~$1
                                                                                                                                                                                          SEND "s* "
:SECTOR~:170
                                                                                                                                                                                          ISEQUAL $SECTOR~$1 $PLAYER~FEDSPACE TRUE
                                                                                                                                                                                          if $SECTOR~$1
                                                                                                                                                                                            SEND "*"
:SECTOR~:172
                                                                                                                                                                                            SEND "** "
:SECTOR~:172
:SECTOR~:173
:SECTOR~:170
:SECTOR~:171
                                                                                                                                                                                            SETVAR $SECTOR~SECTORDATA ""
:SECTOR~SECTORSLINE_CIT_KILL
                                                                                                                                                                                            SETVAR $SECTOR~LINE CURRENTANSILINE
                                                                                                                                                                                            MERGETEXT $SECTOR~LINE $SECTOR~ENDLINE $SECTOR~$3
                                                                                                                                                                                            MERGETEXT $SECTOR~STARTLINE $SECTOR~$3 $SECTOR~$1
                                                                                                                                                                                            SETVAR $SECTOR~LINE $SECTOR~$1
                                                                                                                                                                                            MERGETEXT $SECTOR~SECTORDATA $SECTOR~LINE $SECTOR~$1
                                                                                                                                                                                            SETVAR $SECTOR~SECTORDATA $SECTOR~$1
                                                                                                                                                                                            GETWORDPOS $SECTOR~LINE $SECTOR~POS "Warps to Sector(s) "
                                                                                                                                                                                            ISGREATER $SECTOR~$1 $SECTOR~POS 0
                                                                                                                                                                                            if $SECTOR~$1
                                                                                                                                                                                              goto :GOTSECTORDATA
:SECTOR~:174
                                                                                                                                                                                              SETTEXTLINETRIGGER GETLINE :SECTORSLINE_CIT_KILL
:SECTOR~:174
:SECTOR~:175
                                                                                                                                                                                              PAUSE
:SECTOR~GOTSECTORDATA
                                                                                                                                                                                              GETWORDPOS $SECTOR~SECTORDATA $SECTOR~BEACONPOS "[0m[35mBeacon  [1;33m:"
                                                                                                                                                                                              ISGREATER $SECTOR~$1 $SECTOR~BEACONPOS 0
                                                                                                                                                                                              if $SECTOR~$1
                                                                                                                                                                                                SETVAR $SECTOR~CONTAINSBEACON TRUE
:SECTOR~:176
                                                                                                                                                                                                SETVAR $SECTOR~CONTAINSBEACON FALSE
:SECTOR~:176
:SECTOR~:177
                                                                                                                                                                                                SETVAR $PLAYER~CURRENT_SECTOR CURRENTSECTOR
                                                                                                                                                                                                GOSUB :GETTRADERS
                                                                                                                                                                                                GOSUB :GETEMPTYSHIPS
                                                                                                                                                                                                GOSUB :GETFAKETRADERS
                                                                                                                                                                                                RETURN
:SECTOR~GETEMPTYSHIPS
                                                                                                                                                                                                GETWORDPOS $SECTOR~SECTORDATA $SECTOR~POSSHIPS "[0m[33mShips   [1m:"
                                                                                                                                                                                                ISGREATER $SECTOR~$1 $SECTOR~POSSHIPS 0
                                                                                                                                                                                                if $SECTOR~$1
                                                                                                                                                                                                  GETTEXT $SECTOR~SECTORDATA $SECTOR~SHIPDATA "[0m[33mShips   [1m:" "[0m[1;32mWarps to Sector(s) [33m:"
                                                                                                                                                                                                  MERGETEXT $SECTOR~STARTLINE $SECTOR~SHIPDATA $SECTOR~$1
                                                                                                                                                                                                  SETVAR $SECTOR~SHIPDATA $SECTOR~$1
                                                                                                                                                                                                  GETTEXT $SECTOR~SHIPDATA $SECTOR~TEMP $SECTOR~STARTLINE $SECTOR~ENDLINE
                                                                                                                                                                                                  SETVAR $SECTOR~EMPTYSHIPCOUNT 0
                                                                                                                                                                                                  SETVAR $SECTOR~MYSHIPCOUNT 0
:SECTOR~:180
                                                                                                                                                                                                  ISNOTEQUAL $SECTOR~$1 $SECTOR~TEMP ""
                                                                                                                                                                                                  if $SECTOR~$1
                                                                                                                                                                                                    MERGETEXT $SECTOR~TEMP $SECTOR~ENDLINE $SECTOR~$3
                                                                                                                                                                                                    MERGETEXT $SECTOR~STARTLINE $SECTOR~$3 $SECTOR~$1
                                                                                                                                                                                                    GETLENGTH $SECTOR~$1 $SECTOR~LENGTH
                                                                                                                                                                                                    SETVAR $SECTOR~$1 $SECTOR~LENGTH
                                                                                                                                                                                                    ADD $SECTOR~$1 1
                                                                                                                                                                                                    CUTTEXT $SECTOR~SHIPDATA $SECTOR~SHIPDATA $SECTOR~$1 9999
                                                                                                                                                                                                    STRIPTEXT $SECTOR~TEMP $SECTOR~STARTLINE
                                                                                                                                                                                                    STRIPTEXT $SECTOR~TEMP "  "
                                                                                                                                                                                                    STRIPTEXT $SECTOR~TEMP $SECTOR~ENDLINE
                                                                                                                                                                                                    GETWORDPOS $SECTOR~TEMP $SECTOR~POS2 "[0;35m[[31mOwned by[35m]"
                                                                                                                                                                                                    ISGREATER $SECTOR~$1 $SECTOR~POS2 0
                                                                                                                                                                                                    if $SECTOR~$1
                                                                                                                                                                                                      CUTTEXT $SECTOR~TEMP $SECTOR~TEMP $SECTOR~POS2 9999
                                                                                                                                                                                                      STRIPTEXT $SECTOR~TEMP "[0;35m[[31mOwned by[35m] "
                                                                                                                                                                                                      GETWORDPOS $SECTOR~TEMP $SECTOR~POS3 ",[0;32m w/"
                                                                                                                                                                                                      CUTTEXT $SECTOR~TEMP $SECTOR~TEMP 0 $SECTOR~POS3
                                                                                                                                                                                                      GETWORDPOS $SECTOR~TEMP $SECTOR~POS4 "[34m[[1;36m"
                                                                                                                                                                                                      STRIPTEXT $SECTOR~TEMP "[1;33m,"
                                                                                                                                                                                                      ISGREATER $SECTOR~$1 $SECTOR~POS4 0
                                                                                                                                                                                                      if $SECTOR~$1
                                                                                                                                                                                                        CUTTEXT $SECTOR~TEMP $SECTOR~TEMP $SECTOR~POS4 9999
                                                                                                                                                                                                        STRIPTEXT $SECTOR~TEMP "[34m[[1;36m"
                                                                                                                                                                                                        STRIPTEXT $SECTOR~TEMP "[0;34m]"
:SECTOR~:184
:SECTOR~:185
                                                                                                                                                                                                        SETVAR $SECTOR~$1 $SECTOR~EMPTYSHIPCOUNT
                                                                                                                                                                                                        ADD $SECTOR~$1 1
                                                                                                                                                                                                        SETVAR $PLAYER~EMPTYSHIPS[$SECTOR~$1] $SECTOR~TEMP
                                                                                                                                                                                                        SETVAR $SECTOR~$5 $SECTOR~EMPTYSHIPCOUNT
                                                                                                                                                                                                        ADD $SECTOR~$5 1
                                                                                                                                                                                                        ISEQUAL $SECTOR~$2 $PLAYER~EMPTYSHIPS[$SECTOR~$5] $PLAYER~CORP
                                                                                                                                                                                                        SETVAR $SECTOR~$11 $SECTOR~EMPTYSHIPCOUNT
                                                                                                                                                                                                        ADD $SECTOR~$11 1
                                                                                                                                                                                                        ISEQUAL $SECTOR~$8 $PLAYER~EMPTYSHIPS[$SECTOR~$11] $PLAYER~TRADER_NAME
                                                                                                                                                                                                        SETVAR $SECTOR~$1 $SECTOR~$2
                                                                                                                                                                                                        OR $SECTOR~$1 $SECTOR~$8
                                                                                                                                                                                                        if $SECTOR~$1
                                                                                                                                                                                                          ADD $SECTOR~MYSHIPCOUNT 1
:SECTOR~:186
:SECTOR~:187
                                                                                                                                                                                                          ADD $SECTOR~EMPTYSHIPCOUNT 1
:SECTOR~:182
:SECTOR~:183
                                                                                                                                                                                                          GETTEXT $SECTOR~SHIPDATA $SECTOR~TEMP $SECTOR~STARTLINE $SECTOR~ENDLINE
:SECTOR~:181
:SECTOR~:178
                                                                                                                                                                                                          SETVAR $SECTOR~EMPTYSHIPCOUNT 0
                                                                                                                                                                                                          SETVAR $SECTOR~MYSHIPCOUNT 0
:SECTOR~:178
:SECTOR~:179
                                                                                                                                                                                                          RETURN
:SECTOR~GETFAKETRADERS
                                                                                                                                                                                                          SETVAR $SECTOR~FEDERALSINSECTOR FALSE
                                                                                                                                                                                                          SETVAR $SECTOR~FEDERALCOUNT 0
                                                                                                                                                                                                          GETWORDPOS $SECTOR~SECTORDATA $SECTOR~POSSHIPS "[0m[33mShips   [1m:"
                                                                                                                                                                                                          GETWORDPOS $SECTOR~SECTORDATA $SECTOR~POSTRADERS "[0m[33mTraders [1m:"
                                                                                                                                                                                                          GETWORDPOS $SECTOR~SECTORDATA $SECTOR~POSFEDERALS "[0m[33mFederals[1m:"
                                                                                                                                                                                                          ISGREATER $SECTOR~$1 $SECTOR~POSFEDERALS 0
                                                                                                                                                                                                          if $SECTOR~$1
                                                                                                                                                                                                            SETVAR $SECTOR~FEDERALSINSECTOR TRUE
:SECTOR~:188
:SECTOR~:189
                                                                                                                                                                                                            ISGREATER $SECTOR~$1 $SECTOR~POSTRADERS 0
                                                                                                                                                                                                            if $SECTOR~$1
                                                                                                                                                                                                              GETTEXT $SECTOR~SECTORDATA $SECTOR~FAKEDATA "[1;32mSector  [33m:" "[0m[33mTraders [1m:"
                                                                                                                                                                                                              GOSUB :GRABFAKEDATA
:SECTOR~:190
                                                                                                                                                                                                              ISGREATER $SECTOR~$1 $SECTOR~POSSHIPS 0
                                                                                                                                                                                                              if $SECTOR~$1
                                                                                                                                                                                                                GETTEXT $SECTOR~SECTORDATA $SECTOR~FAKEDATA "[1;32mSector  [33m:" "[0m[33mShips   [1m:"
                                                                                                                                                                                                                GOSUB :GRABFAKEDATA
:SECTOR~:192
                                                                                                                                                                                                                GETTEXT $SECTOR~SECTORDATA $SECTOR~FAKEDATA "[1;32mSector  [33m:" "[0m[1;32mWarps to Sector(s) [33m:"
                                                                                                                                                                                                                GOSUB :GRABFAKEDATA
:SECTOR~:192
:SECTOR~:191
                                                                                                                                                                                                                RETURN
:SECTOR~GRABFAKEDATA
                                                                                                                                                                                                                MERGETEXT $SECTOR~STARTLINE $SECTOR~FAKEDATA $SECTOR~$1
                                                                                                                                                                                                                SETVAR $SECTOR~FAKEDATA $SECTOR~$1
                                                                                                                                                                                                                GETTEXT $SECTOR~FAKEDATA $SECTOR~TEMP $SECTOR~STARTLINE $SECTOR~ENDLINE
                                                                                                                                                                                                                SETVAR $SECTOR~FAKETRADERCOUNT 0
:SECTOR~:193
                                                                                                                                                                                                                ISNOTEQUAL $SECTOR~$1 $SECTOR~TEMP ""
                                                                                                                                                                                                                if $SECTOR~$1
                                                                                                                                                                                                                  MERGETEXT $SECTOR~TEMP $SECTOR~ENDLINE $SECTOR~$3
                                                                                                                                                                                                                  MERGETEXT $SECTOR~STARTLINE $SECTOR~$3 $SECTOR~$1
                                                                                                                                                                                                                  GETLENGTH $SECTOR~$1 $SECTOR~LENGTH
                                                                                                                                                                                                                  SETVAR $SECTOR~$1 $SECTOR~LENGTH
                                                                                                                                                                                                                  ADD $SECTOR~$1 1
                                                                                                                                                                                                                  CUTTEXT $SECTOR~FAKEDATA $SECTOR~FAKEDATA $SECTOR~$1 9999
                                                                                                                                                                                                                  STRIPTEXT $SECTOR~TEMP $SECTOR~STARTLINE
                                                                                                                                                                                                                  STRIPTEXT $SECTOR~TEMP "  "
                                                                                                                                                                                                                  STRIPTEXT $SECTOR~TEMP $SECTOR~ENDLINE
                                                                                                                                                                                                                  GETWORDPOS $SECTOR~TEMP $SECTOR~POS "33m,[0;32m w/ "
                                                                                                                                                                                                                  ISLESSEREQUAL $SECTOR~$1 $SECTOR~POS 0
                                                                                                                                                                                                                  if $SECTOR~$1
                                                                                                                                                                                                                    GETWORDPOS $SECTOR~TEMP $SECTOR~POS "[0;32mw/ "
:SECTOR~:195
:SECTOR~:196
                                                                                                                                                                                                                    GETWORDPOS $SECTOR~TEMP $SECTOR~POS2 "[33m, [0;32mwith"
                                                                                                                                                                                                                    GETWORDPOS $SECTOR~TEMP $SECTOR~POS3 "[0;35m[[31mOwned by[35m]"
                                                                                                                                                                                                                    MERGETEXT #27 "[1;33m" $SECTOR~$3
                                                                                                                                                                                                                    MERGETEXT "[0;32mw/ " $SECTOR~$3 $SECTOR~$1
                                                                                                                                                                                                                    GETWORDPOS $SECTOR~TEMP $SECTOR~POS4 $SECTOR~$1
                                                                                                                                                                                                                    GETWORDPOS $SECTOR~TEMP $SECTOR~POS5 "in[36m "
                                                                                                                                                                                                                    ISGREATER $SECTOR~$3 $SECTOR~POS4 0
                                                                                                                                                                                                                    ISGREATER $SECTOR~$7 $SECTOR~POS 0
                                                                                                                                                                                                                    ISGREATER $SECTOR~$10 $SECTOR~POS2 0
                                                                                                                                                                                                                    SETVAR $SECTOR~$6 $SECTOR~$7
                                                                                                                                                                                                                    OR $SECTOR~$6 $SECTOR~$10
                                                                                                                                                                                                                    SETVAR $SECTOR~$2 $SECTOR~$3
                                                                                                                                                                                                                    OR $SECTOR~$2 $SECTOR~$6
                                                                                                                                                                                                                    ISLESSEREQUAL $SECTOR~$13 $SECTOR~POS3 0
                                                                                                                                                                                                                    SETVAR $SECTOR~$1 $SECTOR~$2
                                                                                                                                                                                                                    AND $SECTOR~$1 $SECTOR~$13
                                                                                                                                                                                                                    if $SECTOR~$1
                                                                                                                                                                                                                      SETVAR $SECTOR~$1 $SECTOR~FAKETRADERCOUNT
                                                                                                                                                                                                                      ADD $SECTOR~$1 1
                                                                                                                                                                                                                      SETVAR $PLAYER~FAKETRADERS[$SECTOR~$1] $SECTOR~TEMP
                                                                                                                                                                                                                      GETWORDPOS $SECTOR~TEMP $SECTOR~POSA "Zyrain"
                                                                                                                                                                                                                      GETWORDPOS $SECTOR~TEMP $SECTOR~POSB "Clausewitz"
                                                                                                                                                                                                                      GETWORDPOS $SECTOR~TEMP $SECTOR~POSC "Nelson"
                                                                                                                                                                                                                      ISGREATER $SECTOR~$2 $SECTOR~POSA 0
                                                                                                                                                                                                                      ISGREATER $SECTOR~$6 $SECTOR~POSB 0
                                                                                                                                                                                                                      ISGREATER $SECTOR~$9 $SECTOR~POSC 0
                                                                                                                                                                                                                      SETVAR $SECTOR~$5 $SECTOR~$6
                                                                                                                                                                                                                      OR $SECTOR~$5 $SECTOR~$9
                                                                                                                                                                                                                      SETVAR $SECTOR~$1 $SECTOR~$2
                                                                                                                                                                                                                      OR $SECTOR~$1 $SECTOR~$5
                                                                                                                                                                                                                      if $SECTOR~$1
                                                                                                                                                                                                                        ADD $SECTOR~FEDERALCOUNT 1
:SECTOR~:199
:SECTOR~:200
                                                                                                                                                                                                                        ADD $SECTOR~FAKETRADERCOUNT 1
:SECTOR~:197
:SECTOR~:198
                                                                                                                                                                                                                        ISGREATER $SECTOR~$1 $SECTOR~POS5 0
                                                                                                                                                                                                                        if $SECTOR~$1
                                                                                                                                                                                                                          GETTEXT $SECTOR~TEMP $SECTOR~SHIPNAME "[1;31m" ")"
                                                                                                                                                                                                                          ISEQUAL $SECTOR~$1 $SECTOR~SHIPNAME ""
                                                                                                                                                                                                                          if $SECTOR~$1
                                                                                                                                                                                                                            MERGETEXT ")" #13 $SECTOR~$1
                                                                                                                                                                                                                            GETTEXT $SECTOR~TEMP $SECTOR~SHIPNAME "(" $SECTOR~$1
                                                                                                                                                                                                                            MERGETEXT $SECTOR~SHIPNAME "ENDOFSHIP" $SECTOR~$1
                                                                                                                                                                                                                            MERGETEXT #27 "[" $SECTOR~$6
                                                                                                                                                                                                                            MERGETEXT "m" $SECTOR~$6 $SECTOR~$4
                                                                                                                                                                                                                            GETTEXT $SECTOR~$1 $SECTOR~SHIPNAME $SECTOR~$4 "ENDOFSHIP"
:SECTOR~:203
:SECTOR~:204
                                                                                                                                                                                                                            MERGETEXT $SECTOR~SHIPNAME "ENDOFSHIP" $SECTOR~$1
                                                                                                                                                                                                                            GETTEXT $SECTOR~$1 $SECTOR~SHIPNAME "m" "ENDOFSHIP"
:SECTOR~:201
:SECTOR~:202
                                                                                                                                                                                                                            GETTEXT $SECTOR~FAKEDATA $SECTOR~TEMP $SECTOR~STARTLINE $SECTOR~ENDLINE
:SECTOR~:194
                                                                                                                                                                                                                            RETURN
:SECTOR~GETTRADERS
                                                                                                                                                                                                                            GETWORDPOS $SECTOR~SECTORDATA $SECTOR~POSTRADER "[0m[33mTraders [1m:"
                                                                                                                                                                                                                            ISGREATER $SECTOR~$1 $SECTOR~POSTRADER 0
                                                                                                                                                                                                                            if $SECTOR~$1
                                                                                                                                                                                                                              GETTEXT $SECTOR~SECTORDATA $SECTOR~TRADERDATA "[0m[33mTraders [1m:" "[0m[1;32mWarps to Sector(s) "
                                                                                                                                                                                                                              MERGETEXT $SECTOR~STARTLINE $SECTOR~TRADERDATA $SECTOR~$1
                                                                                                                                                                                                                              SETVAR $SECTOR~TRADERDATA $SECTOR~$1
                                                                                                                                                                                                                              GETTEXT $SECTOR~TRADERDATA $SECTOR~TEMP $SECTOR~STARTLINE $SECTOR~ENDLINE
                                                                                                                                                                                                                              SETVAR $SECTOR~REALTRADERCOUNT 0
                                                                                                                                                                                                                              SETVAR $SECTOR~CORPIECOUNT 0
                                                                                                                                                                                                                              SETVAR $SECTOR~DEFENDERSHIPS 0
:SECTOR~:207
                                                                                                                                                                                                                              ISNOTEQUAL $SECTOR~$1 $SECTOR~TEMP ""
                                                                                                                                                                                                                              if $SECTOR~$1
                                                                                                                                                                                                                                MERGETEXT $SECTOR~TEMP $SECTOR~ENDLINE $SECTOR~$3
                                                                                                                                                                                                                                MERGETEXT $SECTOR~STARTLINE $SECTOR~$3 $SECTOR~$1
                                                                                                                                                                                                                                GETLENGTH $SECTOR~$1 $SECTOR~LENGTH
                                                                                                                                                                                                                                SETVAR $SECTOR~$1 $SECTOR~LENGTH
                                                                                                                                                                                                                                ADD $SECTOR~$1 1
                                                                                                                                                                                                                                CUTTEXT $SECTOR~TRADERDATA $SECTOR~TRADERDATA $SECTOR~$1 9999
                                                                                                                                                                                                                                STRIPTEXT $SECTOR~TEMP $SECTOR~STARTLINE
                                                                                                                                                                                                                                STRIPTEXT $SECTOR~TEMP $SECTOR~ENDLINE
                                                                                                                                                                                                                                STRIPTEXT $SECTOR~TEMP "[0m          "
                                                                                                                                                                                                                                STRIPTEXT $SECTOR~TEMP "[0m[33mTraders [1m:"
                                                                                                                                                                                                                                SETVAR $SECTOR~J 1
                                                                                                                                                                                                                                SETVAR $SECTOR~ISFOUND FALSE
                                                                                                                                                                                                                                ISLESSEREQUAL $SECTOR~$2 $PLAYER~CURRENT_SECTOR 10
                                                                                                                                                                                                                                ISEQUAL $SECTOR~$5 $PLAYER~CURRENT_SECTOR STARDOCK
                                                                                                                                                                                                                                SETVAR $SECTOR~$1 $SECTOR~$2
                                                                                                                                                                                                                                OR $SECTOR~$1 $SECTOR~$5
                                                                                                                                                                                                                                if $SECTOR~$1
:SECTOR~:211
                                                                                                                                                                                                                                  ISLESSER $SECTOR~$2 $SECTOR~J $PLAYER~RANKSLENGTH
                                                                                                                                                                                                                                  ISEQUAL $SECTOR~$5 $SECTOR~ISFOUND FALSE
                                                                                                                                                                                                                                  SETVAR $SECTOR~$1 $SECTOR~$2
                                                                                                                                                                                                                                  AND $SECTOR~$1 $SECTOR~$5
                                                                                                                                                                                                                                  if $SECTOR~$1
                                                                                                                                                                                                                                    GETWORDPOS $SECTOR~TEMP $SECTOR~POS $PLAYER~RANKS[$SECTOR~J]
                                                                                                                                                                                                                                    ISGREATER $SECTOR~$1 $SECTOR~POS 0
                                                                                                                                                                                                                                    if $SECTOR~$1
                                                                                                                                                                                                                                      GETLENGTH $PLAYER~RANKS[$SECTOR~J] $SECTOR~LENGTH
                                                                                                                                                                                                                                      SETVAR $SECTOR~$3 $SECTOR~LENGTH
                                                                                                                                                                                                                                      ADD $SECTOR~$3 1
                                                                                                                                                                                                                                      SETVAR $SECTOR~$1 $SECTOR~POS
                                                                                                                                                                                                                                      ADD $SECTOR~$1 $SECTOR~$3
                                                                                                                                                                                                                                      CUTTEXT $SECTOR~TEMP $SECTOR~TEMP $SECTOR~$1 9999
                                                                                                                                                                                                                                      ISLESSEREQUAL $SECTOR~$1 $SECTOR~J 10
                                                                                                                                                                                                                                      if $SECTOR~$1
                                                                                                                                                                                                                                        SETVAR $SECTOR~$1 $SECTOR~REALTRADERCOUNT
                                                                                                                                                                                                                                        ADD $SECTOR~$1 1
                                                                                                                                                                                                                                        SETVAR $PLAYER~TRADERS[$SECTOR~$1][2] TRUE
:SECTOR~:215
                                                                                                                                                                                                                                        SETVAR $SECTOR~$1 $SECTOR~REALTRADERCOUNT
                                                                                                                                                                                                                                        ADD $SECTOR~$1 1
                                                                                                                                                                                                                                        SETVAR $PLAYER~TRADERS[$SECTOR~$1][2] FALSE
:SECTOR~:215
:SECTOR~:216
                                                                                                                                                                                                                                        SETVAR $SECTOR~ISFOUND TRUE
:SECTOR~:213
:SECTOR~:214
                                                                                                                                                                                                                                        ADD $SECTOR~J 1
:SECTOR~:212
:SECTOR~:209
                                                                                                                                                                                                                                        SETVAR $SECTOR~$1 $SECTOR~REALTRADERCOUNT
                                                                                                                                                                                                                                        ADD $SECTOR~$1 1
                                                                                                                                                                                                                                        SETVAR $PLAYER~TRADERS[$SECTOR~$1][2] FALSE
:SECTOR~:209
:SECTOR~:210
                                                                                                                                                                                                                                        GETWORDPOS $SECTOR~TEMP $SECTOR~POS "[0;32m w/"
                                                                                                                                                                                                                                        GETWORDPOS $SECTOR~TEMP $SECTOR~POS2 "[0;35m[[31mOwned by[35m]"
                                                                                                                                                                                                                                        MERGETEXT "[32m     in " #27 $SECTOR~$7
                                                                                                                                                                                                                                        MERGETEXT #27 $SECTOR~$7 $SECTOR~$5
                                                                                                                                                                                                                                        MERGETEXT "[0m      " $SECTOR~$5 $SECTOR~$3
                                                                                                                                                                                                                                        MERGETEXT #27 $SECTOR~$3 $SECTOR~$1
                                                                                                                                                                                                                                        GETWORDPOS $SECTOR~TEMP $SECTOR~POS3 $SECTOR~$1
                                                                                                                                                                                                                                        ISGREATER $SECTOR~$2 $SECTOR~POS 0
                                                                                                                                                                                                                                        ISLESSEREQUAL $SECTOR~$5 $SECTOR~POS2 0
                                                                                                                                                                                                                                        SETVAR $SECTOR~$1 $SECTOR~$2
                                                                                                                                                                                                                                        AND $SECTOR~$1 $SECTOR~$5
                                                                                                                                                                                                                                        if $SECTOR~$1
                                                                                                                                                                                                                                          GETWORDPOS $SECTOR~TEMP $SECTOR~POS "[[1;36m"
                                                                                                                                                                                                                                          ISGREATER $SECTOR~$1 $SECTOR~POS 0
                                                                                                                                                                                                                                          if $SECTOR~$1
                                                                                                                                                                                                                                            GETTEXT $SECTOR~TEMP $SECTOR~TEMPCORP "[[1;36m" "[0;34m]"
                                                                                                                                                                                                                                            STRIPTEXT $SECTOR~TEMPCORP ""
:SECTOR~:219
                                                                                                                                                                                                                                            SETVAR $SECTOR~TEMPCORP 99999
:SECTOR~:219
:SECTOR~:220
                                                                                                                                                                                                                                            REPLACETEXT $SECTOR~TEMP "[0;34m" "[34m"
                                                                                                                                                                                                                                            GETWORDPOS $SECTOR~TEMP $SECTOR~POS "[34m"
                                                                                                                                                                                                                                            CUTTEXT $SECTOR~TEMP $SECTOR~TEMP 1 $SECTOR~POS
                                                                                                                                                                                                                                            STRIPTEXT $SECTOR~TEMP ""
                                                                                                                                                                                                                                            LOWERCASE $SECTOR~TEMP
                                                                                                                                                                                                                                            SETVAR $SECTOR~$1 $SECTOR~REALTRADERCOUNT
                                                                                                                                                                                                                                            ADD $SECTOR~$1 1
                                                                                                                                                                                                                                            SETVAR $PLAYER~TRADERS[$SECTOR~$1] $SECTOR~TEMP
                                                                                                                                                                                                                                            SETVAR $SECTOR~$1 $SECTOR~REALTRADERCOUNT
                                                                                                                                                                                                                                            ADD $SECTOR~$1 1
                                                                                                                                                                                                                                            SETVAR $PLAYER~TRADERS[$SECTOR~$1][1] $SECTOR~TEMPCORP
                                                                                                                                                                                                                                            ISEQUAL $SECTOR~$1 $SECTOR~TEMPCORP $PLAYER~CORP
                                                                                                                                                                                                                                            if $SECTOR~$1
                                                                                                                                                                                                                                              ADD $SECTOR~CORPIECOUNT 1
:SECTOR~:221
:SECTOR~:222
                                                                                                                                                                                                                                              ADD $SECTOR~REALTRADERCOUNT 1
:SECTOR~:217
:SECTOR~:218
                                                                                                                                                                                                                                              ISGREATER $SECTOR~$2 $SECTOR~POS3 0
                                                                                                                                                                                                                                              ISNOTEQUAL $SECTOR~$6 $SECTOR~TEMPCORP $PLAYER~CORP
                                                                                                                                                                                                                                              ISNOTEQUAL $SECTOR~$9 $PLAYER~OVERRIDE TRUE
                                                                                                                                                                                                                                              SETVAR $SECTOR~$5 $SECTOR~$6
                                                                                                                                                                                                                                              AND $SECTOR~$5 $SECTOR~$9
                                                                                                                                                                                                                                              SETVAR $SECTOR~$1 $SECTOR~$2
                                                                                                                                                                                                                                              AND $SECTOR~$1 $SECTOR~$5
                                                                                                                                                                                                                                              if $SECTOR~$1
                                                                                                                                                                                                                                                GETTEXT $SECTOR~TEMP $SECTOR~SHIPNAME "(" ")"
                                                                                                                                                                                                                                                ISEQUAL $SECTOR~$1 $SECTOR~SHIPNAME ""
                                                                                                                                                                                                                                                if $SECTOR~$1
                                                                                                                                                                                                                                                  GETTEXT $SECTOR~SHIPNAME $SECTOR~SHIPNAME "(" ")"
:SECTOR~:225
:SECTOR~:226
                                                                                                                                                                                                                                                  MERGETEXT $SECTOR~SHIPNAME "ENDOFSHIP" $SECTOR~$1
                                                                                                                                                                                                                                                  GETTEXT $SECTOR~$1 $SECTOR~SHIPNAME "m" "ENDOFSHIP"
                                                                                                                                                                                                                                                  SETVAR $SECTOR~ISFOUND FALSE
                                                                                                                                                                                                                                                  SETVAR $SECTOR~S 1
                                                                                                                                                                                                                                                  SETVAR $SECTOR~ISDEFENDER FALSE
                                                                                                                                                                                                                                                  REPLACETEXT $SECTOR~SHIPNAME ";" "m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "30m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "31m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "32m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "33m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "34m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "35m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "36m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "37m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "38m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "39m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "40m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "41m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "42m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "43m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "44m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "45m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "46m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "47m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "[0;30;47m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "[32;40m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "[0;"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "[1;"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "[0m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "[1m"
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME #13
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME #27
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME ""
                                                                                                                                                                                                                                                  STRIPTEXT $SECTOR~SHIPNAME "["
:SECTOR~:227
                                                                                                                                                                                                                                                  ISEQUAL $SECTOR~$2 $SECTOR~ISFOUND FALSE
                                                                                                                                                                                                                                                  ISLESSER $SECTOR~$5 $SECTOR~S $SHIP~SHIPCOUNTER
                                                                                                                                                                                                                                                  SETVAR $SECTOR~$1 $SECTOR~$2
                                                                                                                                                                                                                                                  AND $SECTOR~$1 $SECTOR~$5
                                                                                                                                                                                                                                                  if $SECTOR~$1
                                                                                                                                                                                                                                                    STRIPTEXT $SHIP~SHIPLIST[$SECTOR~S] "["
                                                                                                                                                                                                                                                    GETWORDPOS $SECTOR~SHIPNAME $SECTOR~POS $SHIP~SHIPLIST[$SECTOR~S]
                                                                                                                                                                                                                                                    ISGREATER $SECTOR~$1 $SECTOR~POS 0
                                                                                                                                                                                                                                                    if $SECTOR~$1
                                                                                                                                                                                                                                                      SETVAR $SECTOR~ISFOUND TRUE
                                                                                                                                                                                                                                                      SETVAR $SECTOR~ISDEFENDER $SHIP~SHIPLIST[$SECTOR~S][8]
:SECTOR~:229
:SECTOR~:230
                                                                                                                                                                                                                                                      ADD $SECTOR~S 1
:SECTOR~:228
                                                                                                                                                                                                                                                      SETVAR $PLAYER~TRADERS[$SECTOR~REALTRADERCOUNT][3] $SECTOR~SHIPNAME
                                                                                                                                                                                                                                                      ISEQUAL $SECTOR~$1 $SECTOR~ISDEFENDER TRUE
                                                                                                                                                                                                                                                      if $SECTOR~$1
                                                                                                                                                                                                                                                        SETVAR $PLAYER~TRADERS[$SECTOR~REALTRADERCOUNT][1] 100000
                                                                                                                                                                                                                                                        ADD $SECTOR~DEFENDERSHIPS 1
:SECTOR~:231
:SECTOR~:232
:SECTOR~:223
:SECTOR~:224
                                                                                                                                                                                                                                                        GETTEXT $SECTOR~TRADERDATA $SECTOR~TEMP $SECTOR~STARTLINE $SECTOR~ENDLINE
:SECTOR~:208
:SECTOR~:205
                                                                                                                                                                                                                                                        SETVAR $SECTOR~REALTRADERCOUNT 0
                                                                                                                                                                                                                                                        SETVAR $SECTOR~CORPIECOUNT 0
                                                                                                                                                                                                                                                        SETVAR $SECTOR~DEFENDERSHIPS 0
:SECTOR~:205
:SECTOR~:206
                                                                                                                                                                                                                                                        RETURN
:COMBAT~FASTCAPTURE
                                                                                                                                                                                                                                                        SETVAR $PLAYER~ISFOUND FALSE
                                                                                                                                                                                                                                                        SETVAR $COMBAT~TARGETISALIEN FALSE
                                                                                                                                                                                                                                                        SETVAR $COMBAT~STILLSHIELDS FALSE
                                                                                                                                                                                                                                                        LOADVAR $SHIP~SHIP_MAX_ATTACK
                                                                                                                                                                                                                                                        MERGETEXT $PLANET~PLANET "* m * * * q " $COMBAT~$3
                                                                                                                                                                                                                                                        MERGETEXT "l " $COMBAT~$3 $COMBAT~$1
                                                                                                                                                                                                                                                        SETVAR $COMBAT~REFURBSTRING $COMBAT~$1
:COMBAT~CHECKINGFIGS
                                                                                                                                                                                                                                                        ISLESSEREQUAL $COMBAT~$1 $PLAYER~FIGHTERS 0
                                                                                                                                                                                                                                                        if $COMBAT~$1
                                                                                                                                                                                                                                                          GOSUB :PLAYER~QUIKSTATS
                                                                                                                                                                                                                                                          ISLESSEREQUAL $COMBAT~$1 $PLAYER~FIGHTERS 0
                                                                                                                                                                                                                                                          if $COMBAT~$1
                                                                                                                                                                                                                                                            SETVAR $SWITCHBOARD~MESSAGE "No fighters on ship.*"
                                                                                                                                                                                                                                                            GOSUB :SWITCHBOARD~SWITCHBOARD
                                                                                                                                                                                                                                                            goto :CAPSTOPPINGPOINT
:COMBAT~:235
                                                                                                                                                                                                                                                            goto :CHECKINGFIGS
:COMBAT~:235
:COMBAT~:236
:COMBAT~:233
:COMBAT~:234
                                                                                                                                                                                                                                                            ISEQUAL $COMBAT~$1 $PLAYER~STARTINGLOCATION "Citadel"
                                                                                                                                                                                                                                                            if $COMBAT~$1
                                                                                                                                                                                                                                                              SEND "q q * "
:COMBAT~:237
:COMBAT~:238
                                                                                                                                                                                                                                                              SETVAR $COMBAT~TARGETSTRING "a "
                                                                                                                                                                                                                                                              ISGREATER $COMBAT~$3 $SECTOR~FAKETRADERCOUNT 0
                                                                                                                                                                                                                                                              ISEQUAL $COMBAT~$6 $PLAYER~CAPPINGALIENS TRUE
                                                                                                                                                                                                                                                              SETVAR $COMBAT~$2 $COMBAT~$3
                                                                                                                                                                                                                                                              AND $COMBAT~$2 $COMBAT~$6
                                                                                                                                                                                                                                                              ISNOTEQUAL $COMBAT~$10 $PLAYER~ISFOUND TRUE
                                                                                                                                                                                                                                                              ISNOTEQUAL $COMBAT~$13 $PLAYER~EMPTY_SHIPS_ONLY TRUE
                                                                                                                                                                                                                                                              SETVAR $COMBAT~$9 $COMBAT~$10
                                                                                                                                                                                                                                                              AND $COMBAT~$9 $COMBAT~$13
                                                                                                                                                                                                                                                              SETVAR $COMBAT~$1 $COMBAT~$2
                                                                                                                                                                                                                                                              AND $COMBAT~$1 $COMBAT~$9
                                                                                                                                                                                                                                                              if $COMBAT~$1
                                                                                                                                                                                                                                                                ISNOTEQUAL $COMBAT~$1 $PLAYER~FEDSPACE TRUE
                                                                                                                                                                                                                                                                if $COMBAT~$1
                                                                                                                                                                                                                                                                  GETWORDPOS $SECTOR~SECTORDATA $COMBAT~BEACONPOS "[0m[35mBeacon  [1;33m:"
                                                                                                                                                                                                                                                                  ISGREATER $COMBAT~$1 $COMBAT~BEACONPOS 0
                                                                                                                                                                                                                                                                  if $COMBAT~$1
                                                                                                                                                                                                                                                                    MERGETEXT $COMBAT~TARGETSTRING "*" $COMBAT~$1
                                                                                                                                                                                                                                                                    SETVAR $COMBAT~TARGETSTRING $COMBAT~$1
:COMBAT~:243
:COMBAT~:244
:COMBAT~:241
:COMBAT~:242
                                                                                                                                                                                                                                                                    SETVAR $COMBAT~A 1
:COMBAT~:245
                                                                                                                                                                                                                                                                    ISLESSEREQUAL $COMBAT~$2 $COMBAT~A $SECTOR~FAKETRADERCOUNT
                                                                                                                                                                                                                                                                    ISEQUAL $COMBAT~$5 $PLAYER~ISFOUND FALSE
                                                                                                                                                                                                                                                                    SETVAR $COMBAT~$1 $COMBAT~$2
                                                                                                                                                                                                                                                                    AND $COMBAT~$1 $COMBAT~$5
                                                                                                                                                                                                                                                                    if $COMBAT~$1
                                                                                                                                                                                                                                                                      GETWORDPOS $PLAYER~FAKETRADERS[$COMBAT~A] $COMBAT~POS "Zyrain"
                                                                                                                                                                                                                                                                      GETWORDPOS $PLAYER~FAKETRADERS[$COMBAT~A] $COMBAT~POS2 "Clausewitz"
                                                                                                                                                                                                                                                                      GETWORDPOS $PLAYER~FAKETRADERS[$COMBAT~A] $COMBAT~POS3 "Nelson"
                                                                                                                                                                                                                                                                      ISLESSEREQUAL $COMBAT~$2 $COMBAT~POS 0
                                                                                                                                                                                                                                                                      ISLESSEREQUAL $COMBAT~$6 $COMBAT~POS2 0
                                                                                                                                                                                                                                                                      ISLESSEREQUAL $COMBAT~$9 $COMBAT~POS3 0
                                                                                                                                                                                                                                                                      SETVAR $COMBAT~$5 $COMBAT~$6
                                                                                                                                                                                                                                                                      AND $COMBAT~$5 $COMBAT~$9
                                                                                                                                                                                                                                                                      SETVAR $COMBAT~$1 $COMBAT~$2
                                                                                                                                                                                                                                                                      AND $COMBAT~$1 $COMBAT~$5
                                                                                                                                                                                                                                                                      if $COMBAT~$1
                                                                                                                                                                                                                                                                        SETVAR $COMBAT~I 0
                                                                                                                                                                                                                                                                        SETVAR $PLAYER~ISFOUND TRUE
                                                                                                                                                                                                                                                                        SETVAR $COMBAT~TARGETISALIEN TRUE
                                                                                                                                                                                                                                                                        MERGETEXT $COMBAT~TARGETSTRING "zy z" $COMBAT~$1
                                                                                                                                                                                                                                                                        SETVAR $COMBAT~TARGETSTRING $COMBAT~$1
:COMBAT~:247
                                                                                                                                                                                                                                                                        MERGETEXT $COMBAT~TARGETSTRING "* " $COMBAT~$1
                                                                                                                                                                                                                                                                        SETVAR $COMBAT~TARGETSTRING $COMBAT~$1
:COMBAT~:247
:COMBAT~:248
                                                                                                                                                                                                                                                                        ADD $COMBAT~A 1
:COMBAT~:246
:COMBAT~:239
:COMBAT~:240
                                                                                                                                                                                                                                                                        ISEQUAL $COMBAT~$2 $PLAYER~ISFOUND FALSE
                                                                                                                                                                                                                                                                        ISGREATER $COMBAT~$5 $SECTOR~EMPTYSHIPCOUNT 0
                                                                                                                                                                                                                                                                        SETVAR $COMBAT~$1 $COMBAT~$2
                                                                                                                                                                                                                                                                        AND $COMBAT~$1 $COMBAT~$5
                                                                                                                                                                                                                                                                        if $COMBAT~$1
                                                                                                                                                                                                                                                                          ISNOTEQUAL $COMBAT~$1 $PLAYER~FEDSPACE TRUE
                                                                                                                                                                                                                                                                          if $COMBAT~$1
                                                                                                                                                                                                                                                                            GETWORDPOS $SECTOR~SECTORDATA $COMBAT~BEACONPOS "[0m[35mBeacon  [1;33m:"
                                                                                                                                                                                                                                                                            ISGREATER $COMBAT~$1 $COMBAT~BEACONPOS 0
                                                                                                                                                                                                                                                                            if $COMBAT~$1
                                                                                                                                                                                                                                                                              MERGETEXT $COMBAT~TARGETSTRING "*" $COMBAT~$1
                                                                                                                                                                                                                                                                              SETVAR $COMBAT~TARGETSTRING $COMBAT~$1
:COMBAT~:253
:COMBAT~:254
:COMBAT~:251
:COMBAT~:252
                                                                                                                                                                                                                                                                              SETVAR $COMBAT~C 1
                                                                                                                                                                                                                                                                              SETVAR $PLAYER~ISFOUND FALSE
:COMBAT~:255
                                                                                                                                                                                                                                                                              ISLESSEREQUAL $COMBAT~$2 $COMBAT~C $SECTOR~EMPTYSHIPCOUNT
                                                                                                                                                                                                                                                                              ISEQUAL $COMBAT~$5 $PLAYER~ISFOUND FALSE
                                                                                                                                                                                                                                                                              SETVAR $COMBAT~$1 $COMBAT~$2
                                                                                                                                                                                                                                                                              AND $COMBAT~$1 $COMBAT~$5
                                                                                                                                                                                                                                                                              if $COMBAT~$1
                                                                                                                                                                                                                                                                                ISEQUAL $COMBAT~$2 $PLAYER~EMPTYSHIPS[$COMBAT~C] $PLAYER~CORP
                                                                                                                                                                                                                                                                                ISEQUAL $COMBAT~$5 $PLAYER~EMPTYSHIPS[$COMBAT~C] $PLAYER~TRADER_NAME
                                                                                                                                                                                                                                                                                SETVAR $COMBAT~$1 $COMBAT~$2
                                                                                                                                                                                                                                                                                OR $COMBAT~$1 $COMBAT~$5
                                                                                                                                                                                                                                                                                if $COMBAT~$1
                                                                                                                                                                                                                                                                                  MERGETEXT $COMBAT~TARGETSTRING "* " $COMBAT~$1
                                                                                                                                                                                                                                                                                  SETVAR $COMBAT~TARGETSTRING $COMBAT~$1
:COMBAT~:257
                                                                                                                                                                                                                                                                                  SETVAR $PLAYER~ISFOUND TRUE
                                                                                                                                                                                                                                                                                  MERGETEXT $COMBAT~TARGETSTRING "zy z" $COMBAT~$1
                                                                                                                                                                                                                                                                                  SETVAR $COMBAT~TARGETSTRING $COMBAT~$1
:COMBAT~:257
:COMBAT~:258
                                                                                                                                                                                                                                                                                  ADD $COMBAT~C 1
:COMBAT~:256
:COMBAT~:249
:COMBAT~:250
                                                                                                                                                                                                                                                                                  ISGREATER $COMBAT~$2 $SECTOR~REALTRADERCOUNT $SECTOR~CORPIECOUNT
                                                                                                                                                                                                                                                                                  ISNOTEQUAL $COMBAT~$6 $PLAYER~ONLYALIENS TRUE
                                                                                                                                                                                                                                                                                  ISNOTEQUAL $COMBAT~$9 $PLAYER~EMPTY_SHIPS_ONLY TRUE
                                                                                                                                                                                                                                                                                  SETVAR $COMBAT~$5 $COMBAT~$6
                                                                                                                                                                                                                                                                                  AND $COMBAT~$5 $COMBAT~$9
                                                                                                                                                                                                                                                                                  SETVAR $COMBAT~$1 $COMBAT~$2
                                                                                                                                                                                                                                                                                  AND $COMBAT~$1 $COMBAT~$5
                                                                                                                                                                                                                                                                                  if $COMBAT~$1
                                                                                                                                                                                                                                                                                    ISNOTEQUAL $COMBAT~$1 $PLAYER~FEDSPACE TRUE
                                                                                                                                                                                                                                                                                    if $COMBAT~$1
                                                                                                                                                                                                                                                                                      GETWORDPOS $SECTOR~SECTORDATA $COMBAT~BEACONPOS "[0m[35mBeacon  [1;33m:"
                                                                                                                                                                                                                                                                                      ISGREATER $COMBAT~$1 $COMBAT~BEACONPOS 0
                                                                                                                                                                                                                                                                                      if $COMBAT~$1
                                                                                                                                                                                                                                                                                        MERGETEXT $COMBAT~TARGETSTRING "*" $COMBAT~$1
                                                                                                                                                                                                                                                                                        SETVAR $COMBAT~TARGETSTRING $COMBAT~$1
:COMBAT~:263
:COMBAT~:264
:COMBAT~:261
:COMBAT~:262
                                                                                                                                                                                                                                                                                        SETVAR $COMBAT~I 0
:COMBAT~:265
                                                                                                                                                                                                                                                                                        SETVAR $COMBAT~$3 $SECTOR~EMPTYSHIPCOUNT
                                                                                                                                                                                                                                                                                        ADD $COMBAT~$3 $SECTOR~FAKETRADERCOUNT
                                                                                                                                                                                                                                                                                        ISLESSER $COMBAT~$1 $COMBAT~I $COMBAT~$3
                                                                                                                                                                                                                                                                                        if $COMBAT~$1
                                                                                                                                                                                                                                                                                          MERGETEXT $COMBAT~TARGETSTRING "* " $COMBAT~$1
                                                                                                                                                                                                                                                                                          SETVAR $COMBAT~TARGETSTRING $COMBAT~$1
                                                                                                                                                                                                                                                                                          ADD $COMBAT~I 1
:COMBAT~:266
                                                                                                                                                                                                                                                                                          SETVAR $COMBAT~C 1
:COMBAT~:267
                                                                                                                                                                                                                                                                                          ISLESSEREQUAL $COMBAT~$2 $COMBAT~C $SECTOR~REALTRADERCOUNT
                                                                                                                                                                                                                                                                                          ISEQUAL $COMBAT~$5 $PLAYER~ISFOUND FALSE
                                                                                                                                                                                                                                                                                          SETVAR $COMBAT~$1 $COMBAT~$2
                                                                                                                                                                                                                                                                                          AND $COMBAT~$1 $COMBAT~$5
                                                                                                                                                                                                                                                                                          if $COMBAT~$1
                                                                                                                                                                                                                                                                                            ISEQUAL $COMBAT~$2 $PLAYER~FEDSPACE TRUE
                                                                                                                                                                                                                                                                                            ISEQUAL $COMBAT~$5 $PLAYER~TRADERS[$COMBAT~C][2] TRUE
                                                                                                                                                                                                                                                                                            SETVAR $COMBAT~$1 $COMBAT~$2
                                                                                                                                                                                                                                                                                            AND $COMBAT~$1 $COMBAT~$5
                                                                                                                                                                                                                                                                                            if $COMBAT~$1
                                                                                                                                                                                                                                                                                              MERGETEXT $COMBAT~TARGETSTRING "* " $COMBAT~$1
                                                                                                                                                                                                                                                                                              SETVAR $COMBAT~TARGETSTRING $COMBAT~$1
:COMBAT~:269
                                                                                                                                                                                                                                                                                              ISEQUAL $COMBAT~$1 $PLAYER~TRADERS[$COMBAT~C][1] $PLAYER~CORP
                                                                                                                                                                                                                                                                                              if $COMBAT~$1
                                                                                                                                                                                                                                                                                                MERGETEXT $COMBAT~TARGETSTRING "* " $COMBAT~$1
                                                                                                                                                                                                                                                                                                SETVAR $COMBAT~TARGETSTRING $COMBAT~$1
:COMBAT~:271
                                                                                                                                                                                                                                                                                                ISEQUAL $COMBAT~$2 $PLAYER~TARGETINGCORP TRUE
                                                                                                                                                                                                                                                                                                ISNOTEQUAL $COMBAT~$5 $PLAYER~TRADERS[$COMBAT~C][1] $COMBAT~TARGET
                                                                                                                                                                                                                                                                                                SETVAR $COMBAT~$1 $COMBAT~$2
                                                                                                                                                                                                                                                                                                AND $COMBAT~$1 $COMBAT~$5
                                                                                                                                                                                                                                                                                                if $COMBAT~$1
                                                                                                                                                                                                                                                                                                  MERGETEXT $COMBAT~TARGETSTRING "* " $COMBAT~$1
                                                                                                                                                                                                                                                                                                  SETVAR $COMBAT~TARGETSTRING $COMBAT~$1
:COMBAT~:272
                                                                                                                                                                                                                                                                                                  ISEQUAL $COMBAT~$2 $PLAYER~TARGETINGPERSON TRUE
                                                                                                                                                                                                                                                                                                  ISNOTEQUAL $COMBAT~$5 $PLAYER~TRADERS[$COMBAT~C] $COMBAT~TARGET
                                                                                                                                                                                                                                                                                                  SETVAR $COMBAT~$1 $COMBAT~$2
                                                                                                                                                                                                                                                                                                  AND $COMBAT~$1 $COMBAT~$5
                                                                                                                                                                                                                                                                                                  if $COMBAT~$1
                                                                                                                                                                                                                                                                                                    MERGETEXT $COMBAT~TARGETSTRING "* " $COMBAT~$1
                                                                                                                                                                                                                                                                                                    SETVAR $COMBAT~TARGETSTRING $COMBAT~$1
:COMBAT~:273
                                                                                                                                                                                                                                                                                                    SETVAR $PLAYER~ISFOUND TRUE
                                                                                                                                                                                                                                                                                                    MERGETEXT $COMBAT~TARGETSTRING "zy z" $COMBAT~$1
                                                                                                                                                                                                                                                                                                    SETVAR $COMBAT~TARGETSTRING $COMBAT~$1
:COMBAT~:273
:COMBAT~:270
                                                                                                                                                                                                                                                                                                    ADD $COMBAT~C 1
:COMBAT~:268
:COMBAT~:259
:COMBAT~:260
                                                                                                                                                                                                                                                                                                    ISEQUAL $COMBAT~$1 $PLAYER~ISFOUND FALSE
                                                                                                                                                                                                                                                                                                    if $COMBAT~$1
                                                                                                                                                                                                                                                                                                      ECHO "*You have no targets.*"
                                                                                                                                                                                                                                                                                                      goto :CAPSTOPPINGPOINT
:COMBAT~:274
                                                                                                                                                                                                                                                                                                      SETVAR $COMBAT~ATTACKSTRING ""
:COMBAT~CAP_SHIP
                                                                                                                                                                                                                                                                                                      SETVAR $COMBAT~UNMANNED FALSE
                                                                                                                                                                                                                                                                                                      SETVAR $COMBAT~OWN_ODDS $SHIP~SHIP_OFFENSIVE_ODDS
                                                                                                                                                                                                                                                                                                      SETVAR $COMBAT~CAP_POINTS 0
                                                                                                                                                                                                                                                                                                      SETVAR $COMBAT~MAX_FIGS 0
                                                                                                                                                                                                                                                                                                      SETVAR $COMBAT~CAP_SHIELD_POINTS 0
                                                                                                                                                                                                                                                                                                      SETVAR $COMBAT~SHIP_FIGHTERS 0
                                                                                                                                                                                                                                                                                                      SETVAR $PLAYER~LASTTARGET ""
                                                                                                                                                                                                                                                                                                      SETVAR $COMBAT~FIRSTLOOP TRUE
:COMBAT~:276
                                                                                                                                                                                                                                                                                                      ISGREATER $COMBAT~$1 $PLAYER~FIGHTERS 0
                                                                                                                                                                                                                                                                                                      if $COMBAT~$1
                                                                                                                                                                                                                                                                                                        KILLALLTRIGGERS
                                                                                                                                                                                                                                                                                                        SETVAR $COMBAT~STILLSHIELDS FALSE
                                                                                                                                                                                                                                                                                                        SETVAR $COMBAT~ISSAMETARGET FALSE
:COMBAT~CGOAHEAD
                                                                                                                                                                                                                                                                                                        KILLTRIGGER CHECKCAPTARGET
                                                                                                                                                                                                                                                                                                        SETTEXTTRIGGER FOUNDCAPTARGET :FOUNDCAPTARGET "(Y/N) [N]? Y"
                                                                                                                                                                                                                                                                                                        SETTEXTTRIGGER CHECKCAPTARGET :CHECKCAPTARGET "Yes"
                                                                                                                                                                                                                                                                                                        SETTEXTLINETRIGGER NOCTARGET :NOCAPPINGTARGETS "Do you want instructions (Y/N) [N]?"
                                                                                                                                                                                                                                                                                                        SEND $COMBAT~TARGETSTRING
                                                                                                                                                                                                                                                                                                        PAUSE
                                                                                                                                                                                                                                                                                                        PAUSE
:COMBAT~CHECKCAPTARGET
                                                                                                                                                                                                                                                                                                        GETWORDPOS CURRENTANSILINE $COMBAT~POS "36mYes"
                                                                                                                                                                                                                                                                                                        ISGREATER $COMBAT~$1 $COMBAT~POS 0
                                                                                                                                                                                                                                                                                                        if $COMBAT~$1
                                                                                                                                                                                                                                                                                                          goto :FOUNDCAPTARGET
:COMBAT~:278
                                                                                                                                                                                                                                                                                                          SETTEXTTRIGGER CHECKCAPTARGET :CHECKCAPTARGET "Yes"
                                                                                                                                                                                                                                                                                                          PAUSE
                                                                                                                                                                                                                                                                                                          PAUSE
:COMBAT~:278
:COMBAT~:279
:COMBAT~FOUNDCAPTARGET
                                                                                                                                                                                                                                                                                                          KILLTRIGGER NOCTARGET
                                                                                                                                                                                                                                                                                                          KILLTRIGGER FOUNDCAPTARGET
                                                                                                                                                                                                                                                                                                          KILLTRIGGER CHECKCAPTARGET
                                                                                                                                                                                                                                                                                                          SETVAR $COMBAT~CAP_SHIP_INFO CURRENTLINE
                                                                                                                                                                                                                                                                                                          SETVAR $COMBAT~THISTARGET CURRENTANSILINE
                                                                                                                                                                                                                                                                                                          GETWORD $COMBAT~CAP_SHIP_INFO $COMBAT~ATTACK_PROMPT 1
                                                                                                                                                                                                                                                                                                          ISNOTEQUAL $COMBAT~$1 $COMBAT~ATTACK_PROMPT "Attack"
                                                                                                                                                                                                                                                                                                          if $COMBAT~$1
                                                                                                                                                                                                                                                                                                            KILLALLTRIGGERS
                                                                                                                                                                                                                                                                                                            RETURN
:COMBAT~:280
:COMBAT~:281
                                                                                                                                                                                                                                                                                                            GETWORDPOS $COMBAT~THISTARGET $COMBAT~POS "[0;33m([1;36m"
                                                                                                                                                                                                                                                                                                            CUTTEXT $COMBAT~THISTARGET $COMBAT~THISTARGET 1 $COMBAT~POS
                                                                                                                                                                                                                                                                                                            ISGREATER $COMBAT~$1 $COMBAT~POS 0
                                                                                                                                                                                                                                                                                                            if $COMBAT~$1
                                                                                                                                                                                                                                                                                                              SETVAR $COMBAT~THISTARGET $COMBAT~CAP_SHIP_INFO
                                                                                                                                                                                                                                                                                                              SETVAR $COMBAT~TEMP $COMBAT~THISTARGET
                                                                                                                                                                                                                                                                                                              GETWORDPOS $COMBAT~TEMP $COMBAT~POS " ("
                                                                                                                                                                                                                                                                                                              SETVAR $COMBAT~END_OF_LINE_POS 0
:COMBAT~:284
                                                                                                                                                                                                                                                                                                              ISGREATER $COMBAT~$1 $COMBAT~POS 0
                                                                                                                                                                                                                                                                                                              if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                SETVAR $COMBAT~TARGETPOS $COMBAT~POS
                                                                                                                                                                                                                                                                                                                CUTTEXT $COMBAT~TEMP $COMBAT~POSSIBLETARGET 1 $COMBAT~POS
                                                                                                                                                                                                                                                                                                                REPLACETEXT $COMBAT~TEMP $COMBAT~POSSIBLETARGET ""
                                                                                                                                                                                                                                                                                                                GETWORDPOS $COMBAT~TEMP $COMBAT~POS " ("
                                                                                                                                                                                                                                                                                                                ISGREATER $COMBAT~$1 $COMBAT~POS 0
                                                                                                                                                                                                                                                                                                                if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                  SETVAR $COMBAT~$1 $COMBAT~TARGETPOS
                                                                                                                                                                                                                                                                                                                  ADD $COMBAT~$1 1
                                                                                                                                                                                                                                                                                                                  ADD $COMBAT~END_OF_LINE_POS $COMBAT~$1
:COMBAT~:286
:COMBAT~:287
:COMBAT~:285
                                                                                                                                                                                                                                                                                                                  ISLESSEREQUAL $COMBAT~$1 $COMBAT~END_OF_LINE_POS 0
                                                                                                                                                                                                                                                                                                                  if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                    GETWORDPOS $COMBAT~THISTARGET $COMBAT~END_OF_LINE_POS " (Y"
:COMBAT~:288
:COMBAT~:289
                                                                                                                                                                                                                                                                                                                    CUTTEXT $COMBAT~THISTARGET $COMBAT~THISTARGET 1 $COMBAT~END_OF_LINE_POS
:COMBAT~:282
:COMBAT~:283
                                                                                                                                                                                                                                                                                                                    ISEQUAL $COMBAT~$2 $COMBAT~THISTARGET $PLAYER~LASTTARGET
                                                                                                                                                                                                                                                                                                                    ISNOTEQUAL $COMBAT~$5 $COMBAT~FIRSTLOOP TRUE
                                                                                                                                                                                                                                                                                                                    SETVAR $COMBAT~$1 $COMBAT~$2
                                                                                                                                                                                                                                                                                                                    AND $COMBAT~$1 $COMBAT~$5
                                                                                                                                                                                                                                                                                                                    if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                      SETVAR $COMBAT~ISSAMETARGET TRUE
:COMBAT~:290
                                                                                                                                                                                                                                                                                                                      ISEQUAL $COMBAT~$1 $PLAYER~LASTTARGET ""
                                                                                                                                                                                                                                                                                                                      if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                        SETVAR $PLAYER~LASTTARGET $COMBAT~THISTARGET
                                                                                                                                                                                                                                                                                                                        SETVAR $COMBAT~FIRSTLOOP FALSE
:COMBAT~:292
                                                                                                                                                                                                                                                                                                                        goto :NOCAPPINGTARGETS
:COMBAT~:292
:COMBAT~:291
                                                                                                                                                                                                                                                                                                                        if $COMBAT~ISSAMETARGET
                                                                                                                                                                                                                                                                                                                          goto :SEND_ATTACK
:COMBAT~:293
:COMBAT~:294
:COMBAT~SHIP_TYPE
                                                                                                                                                                                                                                                                                                                          SETVAR $COMBAT~TYPE_COUNT 0
                                                                                                                                                                                                                                                                                                                          SETVAR $COMBAT~IS_SHIP 0
:COMBAT~:295
                                                                                                                                                                                                                                                                                                                          ISLESSER $COMBAT~$1 $COMBAT~TYPE_COUNT $SHIP~SHIPCOUNTER
                                                                                                                                                                                                                                                                                                                          if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                            ADD $COMBAT~TYPE_COUNT 1
                                                                                                                                                                                                                                                                                                                            GETWORDPOS $COMBAT~CAP_SHIP_INFO $COMBAT~IS_SHIP $SHIP~SHIPLIST[$COMBAT~TYPE_COUNT]
                                                                                                                                                                                                                                                                                                                            GETWORDPOS $COMBAT~CAP_SHIP_INFO $COMBAT~UNMAN "'s unmanned"
                                                                                                                                                                                                                                                                                                                            ISGREATER $COMBAT~$1 $COMBAT~UNMAN 0
                                                                                                                                                                                                                                                                                                                            if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                              SETVAR $COMBAT~UNMANNED TRUE
:COMBAT~:297
                                                                                                                                                                                                                                                                                                                              SETVAR $COMBAT~UNMANNED FALSE
:COMBAT~:297
:COMBAT~:298
                                                                                                                                                                                                                                                                                                                              ISGREATER $COMBAT~$2 $COMBAT~IS_SHIP 0
                                                                                                                                                                                                                                                                                                                              ISNOTEQUAL $COMBAT~$5 $SHIP~SHIPLIST[$COMBAT~TYPE_COUNT] 0
                                                                                                                                                                                                                                                                                                                              SETVAR $COMBAT~$1 $COMBAT~$2
                                                                                                                                                                                                                                                                                                                              AND $COMBAT~$1 $COMBAT~$5
                                                                                                                                                                                                                                                                                                                              if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                GETWORD $SHIP~SHIP[$SHIP~SHIPLIST[$COMBAT~TYPE_COUNT]] $PLAYER~SHIELDS 1
                                                                                                                                                                                                                                                                                                                                GETWORD $SHIP~SHIP[$SHIP~SHIPLIST[$COMBAT~TYPE_COUNT]] $COMBAT~DEFODDS 2
                                                                                                                                                                                                                                                                                                                                goto :SEND_ATTACK
:COMBAT~:299
:COMBAT~:300
:COMBAT~:296
                                                                                                                                                                                                                                                                                                                                SETVAR $PLAYER~SHIELDS 16000
                                                                                                                                                                                                                                                                                                                                SETVAR $COMBAT~DEFODDS 5
                                                                                                                                                                                                                                                                                                                                goto :SEND_ATTACK
                                                                                                                                                                                                                                                                                                                                SETVAR $SWITCHBOARD~MESSAGE "Unknown ship type, cannot calculate attack, you must do it manually.*"
                                                                                                                                                                                                                                                                                                                                GOSUB :SWITCHBOARD~SWITCHBOARD
                                                                                                                                                                                                                                                                                                                                SEND "* "
                                                                                                                                                                                                                                                                                                                                RETURN
:COMBAT~SEND_ATTACK
                                                                                                                                                                                                                                                                                                                                KILLTRIGGER FOUNDCAPTARGET
                                                                                                                                                                                                                                                                                                                                KILLTRIGGER NOCTARGET
                                                                                                                                                                                                                                                                                                                                KILLTRIGGER COMBAT
                                                                                                                                                                                                                                                                                                                                KILLTRIGGER CAP_IT
                                                                                                                                                                                                                                                                                                                                KILLTRIGGER NOTARGET
                                                                                                                                                                                                                                                                                                                                KILLTRIGGER NOTARGET2
                                                                                                                                                                                                                                                                                                                                KILLTRIGGER NOCOMBAT
                                                                                                                                                                                                                                                                                                                                KILLTRIGGER THEYATTACKED
                                                                                                                                                                                                                                                                                                                                GETTEXT $COMBAT~CAP_SHIP_INFO $COMBAT~SHIP_FIGHTERS $SHIP~SHIPLIST[$COMBAT~TYPE_COUNT] "(Y/N)"
                                                                                                                                                                                                                                                                                                                                ISEQUAL $COMBAT~$1 $COMBAT~SHIP_FIGHTERS ""
                                                                                                                                                                                                                                                                                                                                if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                  GETTEXT $COMBAT~CAP_SHIP_INFO $COMBAT~SHIP_FIGHTERS " (" ") (Y/N)"
:COMBAT~:301
:COMBAT~:302
                                                                                                                                                                                                                                                                                                                                  GETTEXT $COMBAT~SHIP_FIGHTERS $COMBAT~SHIP_FIGHTERS "-" ")"
                                                                                                                                                                                                                                                                                                                                  STRIPTEXT $COMBAT~SHIP_FIGHTERS ","
                                                                                                                                                                                                                                                                                                                                  SETVAR $COMBAT~SHIP_SHIELD_PERCENT 0
                                                                                                                                                                                                                                                                                                                                  SETVAR $COMBAT~SHIELDPOINTS 0
                                                                                                                                                                                                                                                                                                                                  SETTEXTLINETRIGGER COMBAT :COMBAT_SCAN "Combat scanners show enemy shields at"
                                                                                                                                                                                                                                                                                                                                  SETTEXTTRIGGER NOCOMBAT :CAP_IT "How many fighters do you wish to use"
                                                                                                                                                                                                                                                                                                                                  SETTEXTLINETRIGGER NOTARGET :NOCAPPINGTARGETS "Do you want instructions (Y/N) [N]?"
                                                                                                                                                                                                                                                                                                                                  SETTEXTLINETRIGGER NOTARGET2 :NOCAPPINGTARGETS "'s unmanned"
                                                                                                                                                                                                                                                                                                                                  SETTEXTLINETRIGGER THEYATTACKED :THEYATTACKED "Shipboard Computers "
                                                                                                                                                                                                                                                                                                                                  PAUSE
                                                                                                                                                                                                                                                                                                                                  PAUSE
:COMBAT~COMBAT_SCAN
                                                                                                                                                                                                                                                                                                                                  GETWORD CURRENTLINE $COMBAT~SHIELDPERC 7
                                                                                                                                                                                                                                                                                                                                  STRIPTEXT $COMBAT~SHIELDPERC %
                                                                                                                                                                                                                                                                                                                                  SETVAR $COMBAT~$2 $PLAYER~SHIELDS
                                                                                                                                                                                                                                                                                                                                  MULTIPLY $COMBAT~$2 $COMBAT~SHIELDPERC
                                                                                                                                                                                                                                                                                                                                  SETVAR $COMBAT~$1 $COMBAT~$2
                                                                                                                                                                                                                                                                                                                                  DIVIDE $COMBAT~$1 100
                                                                                                                                                                                                                                                                                                                                  SETVAR $COMBAT~SHIELDPOINTS $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                  SETVAR $COMBAT~STILLSHIELDS TRUE
                                                                                                                                                                                                                                                                                                                                  PAUSE
                                                                                                                                                                                                                                                                                                                                  PAUSE
:COMBAT~THEYATTACKED
                                                                                                                                                                                                                                                                                                                                  ECHO "*They attacked me, switching to 1 fighter attacks.*"
                                                                                                                                                                                                                                                                                                                                  SETVAR $COMBAT~SHIP_FIGHTERS 1
:COMBAT~CAP_IT
                                                                                                                                                                                                                                                                                                                                  KILLTRIGGER COMBAT_SCAN
                                                                                                                                                                                                                                                                                                                                  KILLTRIGGER CAP_IT
                                                                                                                                                                                                                                                                                                                                  KILLTRIGGER NOTARGET
                                                                                                                                                                                                                                                                                                                                  KILLTRIGGER THEYATTACKED
                                                                                                                                                                                                                                                                                                                                  GETWORD CURRENTLINE $COMBAT~MAX_FIGS 11 $SHIP~SHIP_MAX_ATTACK
                                                                                                                                                                                                                                                                                                                                  STRIPTEXT $COMBAT~MAX_FIGS ","
                                                                                                                                                                                                                                                                                                                                  STRIPTEXT $COMBAT~MAX_FIGS ")"
                                                                                                                                                                                                                                                                                                                                  ISEQUAL $COMBAT~$1 $COMBAT~SHIP_FIGHTERS ""
                                                                                                                                                                                                                                                                                                                                  if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                    SETVAR $COMBAT~SHIP_FIGHTERS 1
:COMBAT~:303
:COMBAT~:304
                                                                                                                                                                                                                                                                                                                                    SETVAR $COMBAT~$2 $COMBAT~SHIELDPOINTS
                                                                                                                                                                                                                                                                                                                                    ADD $COMBAT~$2 $COMBAT~SHIP_FIGHTERS
                                                                                                                                                                                                                                                                                                                                    SETVAR $COMBAT~$1 $COMBAT~$2
                                                                                                                                                                                                                                                                                                                                    MULTIPLY $COMBAT~$1 $COMBAT~DEFODDS
                                                                                                                                                                                                                                                                                                                                    SETVAR $COMBAT~CAP_POINTS $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                    ISEQUAL $COMBAT~$3 $PLAYER~DEFENDERCAPPING TRUE
                                                                                                                                                                                                                                                                                                                                    ISNOTEQUAL $COMBAT~$6 $COMBAT~UNMANNED TRUE
                                                                                                                                                                                                                                                                                                                                    SETVAR $COMBAT~$2 $COMBAT~$3
                                                                                                                                                                                                                                                                                                                                    AND $COMBAT~$2 $COMBAT~$6
                                                                                                                                                                                                                                                                                                                                    ISEQUAL $COMBAT~$9 $COMBAT~TARGETISALIEN TRUE
                                                                                                                                                                                                                                                                                                                                    SETVAR $COMBAT~$1 $COMBAT~$2
                                                                                                                                                                                                                                                                                                                                    AND $COMBAT~$1 $COMBAT~$9
                                                                                                                                                                                                                                                                                                                                    if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                      ISEQUAL $COMBAT~$1 $COMBAT~STILLSHIELDS TRUE
                                                                                                                                                                                                                                                                                                                                      if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                        ISGREATER $COMBAT~$1 $COMBAT~SHIP_FIGHTERS 1000
                                                                                                                                                                                                                                                                                                                                        if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                          SETVAR $COMBAT~$2 $COMBAT~SHIELDPOINTS
                                                                                                                                                                                                                                                                                                                                          DIVIDE $COMBAT~$2 $COMBAT~OWN_ODDS
                                                                                                                                                                                                                                                                                                                                          SETVAR $COMBAT~$5 $COMBAT~CAP_POINTS
                                                                                                                                                                                                                                                                                                                                          DIVIDE $COMBAT~$5 100
                                                                                                                                                                                                                                                                                                                                          SETVAR $COMBAT~$1 $COMBAT~$2
                                                                                                                                                                                                                                                                                                                                          ADD $COMBAT~$1 $COMBAT~$5
                                                                                                                                                                                                                                                                                                                                          SETVAR $COMBAT~CAP_POINTS $COMBAT~$1
:COMBAT~:309
                                                                                                                                                                                                                                                                                                                                          SETVAR $COMBAT~$1 $COMBAT~SHIELDPOINTS
                                                                                                                                                                                                                                                                                                                                          ADD $COMBAT~$1 1
                                                                                                                                                                                                                                                                                                                                          SETVAR $COMBAT~CAP_POINTS $COMBAT~$1
:COMBAT~:309
:COMBAT~:310
:COMBAT~:307
                                                                                                                                                                                                                                                                                                                                          SETVAR $COMBAT~CAP_POINTS 1
:COMBAT~:307
:COMBAT~:308
:COMBAT~:305
                                                                                                                                                                                                                                                                                                                                          SETVAR $COMBAT~$1 $COMBAT~CAP_POINTS
                                                                                                                                                                                                                                                                                                                                          DIVIDE $COMBAT~$1 $COMBAT~OWN_ODDS
                                                                                                                                                                                                                                                                                                                                          SETVAR $COMBAT~CAP_POINTS $COMBAT~$1
:COMBAT~:305
:COMBAT~:306
                                                                                                                                                                                                                                                                                                                                          ISEQUAL $COMBAT~$1 $COMBAT~UNMANNED TRUE
                                                                                                                                                                                                                                                                                                                                          if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                            DIVIDE $COMBAT~CAP_POINTS 2
:COMBAT~:311
:COMBAT~:312
                                                                                                                                                                                                                                                                                                                                            SETVAR $COMBAT~$2 $COMBAT~CAP_POINTS
                                                                                                                                                                                                                                                                                                                                            MULTIPLY $COMBAT~$2 78
                                                                                                                                                                                                                                                                                                                                            SETVAR $COMBAT~$1 $COMBAT~$2
                                                                                                                                                                                                                                                                                                                                            DIVIDE $COMBAT~$1 100
                                                                                                                                                                                                                                                                                                                                            SETVAR $COMBAT~CAP_POINTS $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                            ISLESSEREQUAL $COMBAT~$1 $COMBAT~CAP_POINTS 0
                                                                                                                                                                                                                                                                                                                                            if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                              SETVAR $COMBAT~CAP_POINTS 1
:COMBAT~:313
                                                                                                                                                                                                                                                                                                                                              ISGREATER $COMBAT~$1 $COMBAT~CAP_POINTS $COMBAT~MAX_FIGS
                                                                                                                                                                                                                                                                                                                                              if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                                SETVAR $COMBAT~CAP_POINTS $COMBAT~MAX_FIGS
:COMBAT~:315
:COMBAT~:314
                                                                                                                                                                                                                                                                                                                                                MERGETEXT $COMBAT~CAP_POINTS "*  " $COMBAT~$3
                                                                                                                                                                                                                                                                                                                                                MERGETEXT "z" $COMBAT~$3 $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                                SETVAR $COMBAT~SENDATTACK $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                                ISEQUAL $COMBAT~$1 $PLAYER~STARTINGLOCATION "Citadel"
                                                                                                                                                                                                                                                                                                                                                if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                                  MERGETEXT $COMBAT~SENDATTACK $COMBAT~REFURBSTRING $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                                  SETVAR $COMBAT~SENDATTACK $COMBAT~$1
:COMBAT~:316
:COMBAT~:317
                                                                                                                                                                                                                                                                                                                                                  SEND $COMBAT~SENDATTACK
                                                                                                                                                                                                                                                                                                                                                  ISEQUAL $COMBAT~$1 $COMBAT~CAP_POINTS 1
                                                                                                                                                                                                                                                                                                                                                  if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                                    SETVAR $COMBAT~I 1
                                                                                                                                                                                                                                                                                                                                                    SETVAR $COMBAT~BURST ""
:COMBAT~:320
                                                                                                                                                                                                                                                                                                                                                    ISLESSEREQUAL $COMBAT~$1 $COMBAT~I 10
                                                                                                                                                                                                                                                                                                                                                    if $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                                      MERGETEXT $COMBAT~TARGETSTRING $COMBAT~SENDATTACK $COMBAT~$5
                                                                                                                                                                                                                                                                                                                                                      MERGETEXT " " $COMBAT~$5 $COMBAT~$3
                                                                                                                                                                                                                                                                                                                                                      MERGETEXT $COMBAT~BURST $COMBAT~$3 $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                                      SETVAR $COMBAT~BURST $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                                      SETVAR $COMBAT~$1 $PLAYER~FIGHTERS
                                                                                                                                                                                                                                                                                                                                                      SUBTRACT $COMBAT~$1 $COMBAT~CAP_POINTS
                                                                                                                                                                                                                                                                                                                                                      SETVAR $PLAYER~FIGHTERS $COMBAT~$1
                                                                                                                                                                                                                                                                                                                                                      ADD $COMBAT~I 1
:COMBAT~:321
                                                                                                                                                                                                                                                                                                                                                      SEND $COMBAT~BURST
                                                                                                                                                                                                                                                                                                                                                      GOSUB :PLAYER~QUIKSTATS
:COMBAT~:318
:COMBAT~:319
:COMBAT~KEEPCAPPING
:COMBAT~:277
:COMBAT~:274
:COMBAT~:275
                                                                                                                                                                                                                                                                                                                                                      goto :CAPSTOPPINGPOINT
:COMBAT~NOCAPPINGTARGETS
                                                                                                                                                                                                                                                                                                                                                      KILLTRIGGER NOCTARGET
                                                                                                                                                                                                                                                                                                                                                      KILLTRIGGER FOUNDCAPTARGET
                                                                                                                                                                                                                                                                                                                                                      KILLTRIGGER COMBAT_SCAN
                                                                                                                                                                                                                                                                                                                                                      KILLTRIGGER CAP_IT
                                                                                                                                                                                                                                                                                                                                                      KILLTRIGGER NOTARGET
                                                                                                                                                                                                                                                                                                                                                      KILLTRIGGER NOTARGET2
                                                                                                                                                                                                                                                                                                                                                      KILLTRIGGER THEYATTACKED
                                                                                                                                                                                                                                                                                                                                                      SEND "* "
:COMBAT~CAPSTOPPINGPOINT
                                                                                                                                                                                                                                                                                                                                                      KILLALLTRIGGERS
                                                                                                                                                                                                                                                                                                                                                      RETURN
                                                                                                                                                                                                                                                                                                                                                    end
                                                                                                                                                                                                                                                                                                                                                  end
                                                                                                                                                                                                                                                                                                                                                end
                                                                                                                                                                                                                                                                                                                                              end
                                                                                                                                                                                                                                                                                                                                            end
                                                                                                                                                                                                                                                                                                                                          end
                                                                                                                                                                                                                                                                                                                                        end
                                                                                                                                                                                                                                                                                                                                      end
                                                                                                                                                                                                                                                                                                                                    end
                                                                                                                                                                                                                                                                                                                                  end
                                                                                                                                                                                                                                                                                                                                end
                                                                                                                                                                                                                                                                                                                              end
                                                                                                                                                                                                                                                                                                                            end
                                                                                                                                                                                                                                                                                                                          end
                                                                                                                                                                                                                                                                                                                        end
                                                                                                                                                                                                                                                                                                                      end
                                                                                                                                                                                                                                                                                                                    end
                                                                                                                                                                                                                                                                                                                  end
                                                                                                                                                                                                                                                                                                                end
                                                                                                                                                                                                                                                                                                              end
                                                                                                                                                                                                                                                                                                            end
                                                                                                                                                                                                                                                                                                          end
                                                                                                                                                                                                                                                                                                        end
                                                                                                                                                                                                                                                                                                      end
                                                                                                                                                                                                                                                                                                    end
                                                                                                                                                                                                                                                                                                  end
                                                                                                                                                                                                                                                                                                end
                                                                                                                                                                                                                                                                                              end
                                                                                                                                                                                                                                                                                            end
                                                                                                                                                                                                                                                                                          end
                                                                                                                                                                                                                                                                                        end
                                                                                                                                                                                                                                                                                      end
                                                                                                                                                                                                                                                                                    end
                                                                                                                                                                                                                                                                                  end
                                                                                                                                                                                                                                                                                end
                                                                                                                                                                                                                                                                              end
                                                                                                                                                                                                                                                                            end
                                                                                                                                                                                                                                                                          end
                                                                                                                                                                                                                                                                        end
                                                                                                                                                                                                                                                                      end
                                                                                                                                                                                                                                                                    end
                                                                                                                                                                                                                                                                  end
                                                                                                                                                                                                                                                                end
                                                                                                                                                                                                                                                              end
                                                                                                                                                                                                                                                            end
                                                                                                                                                                                                                                                          end
                                                                                                                                                                                                                                                        end
                                                                                                                                                                                                                                                      end
                                                                                                                                                                                                                                                    end
                                                                                                                                                                                                                                                  end
                                                                                                                                                                                                                                                end
                                                                                                                                                                                                                                              end
                                                                                                                                                                                                                                            end
                                                                                                                                                                                                                                          end
                                                                                                                                                                                                                                        end
                                                                                                                                                                                                                                      end
                                                                                                                                                                                                                                    end
                                                                                                                                                                                                                                  end
                                                                                                                                                                                                                                end
                                                                                                                                                                                                                              end
                                                                                                                                                                                                                            end
                                                                                                                                                                                                                          end
                                                                                                                                                                                                                        end
                                                                                                                                                                                                                      end
                                                                                                                                                                                                                    end
                                                                                                                                                                                                                  end
                                                                                                                                                                                                                end
                                                                                                                                                                                                              end
                                                                                                                                                                                                            end
                                                                                                                                                                                                          end
                                                                                                                                                                                                        end
                                                                                                                                                                                                      end
                                                                                                                                                                                                    end
                                                                                                                                                                                                  end
                                                                                                                                                                                                end
                                                                                                                                                                                              end
                                                                                                                                                                                            end
                                                                                                                                                                                          end
                                                                                                                                                                                        end
                                                                                                                                                                                      end
                                                                                                                                                                                    end
                                                                                                                                                                                  end
                                                                                                                                                                                end
                                                                                                                                                                              end
                                                                                                                                                                            end
                                                                                                                                                                          end
                                                                                                                                                                        end
                                                                                                                                                                      end
                                                                                                                                                                    end
                                                                                                                                                                  end
                                                                                                                                                                end
                                                                                                                                                              end
                                                                                                                                                            end
                                                                                                                                                          end
                                                                                                                                                        end
                                                                                                                                                      end
                                                                                                                                                    end
                                                                                                                                                  end
                                                                                                                                                end
                                                                                                                                              end
                                                                                                                                            end
                                                                                                                                          end
                                                                                                                                        end
                                                                                                                                      end
                                                                                                                                    end
                                                                                                                                  end
                                                                                                                                end
                                                                                                                              end
                                                                                                                            end
                                                                                                                          end
                                                                                                                        end
                                                                                                                      end
                                                                                                                    end
                                                                                                                  end
                                                                                                                end
                                                                                                              end
                                                                                                            end
                                                                                                          end
                                                                                                        end
                                                                                                      end
                                                                                                    end
                                                                                                  end
                                                                                                end
                                                                                              end
                                                                                            end
                                                                                          end
                                                                                        end
                                                                                      end
                                                                                    end
                                                                                  end
                                                                                end
                                                                              end
                                                                            end
                                                                          end
                                                                        end
                                                                      end
                                                                    end
                                                                  end
                                                                end
                                                              end
                                                            end
                                                          end
                                                        end
                                                      end
                                                    end
                                                  end
                                                end
                                              end
                                            end
                                          end
                                        end
                                      end
                                    end
                                  end
                                end
                              end
                            end
                          end
                        end
                      end
                    end
                  end
                end
              end
            end
          end
        end
      end
    end
  end
end
