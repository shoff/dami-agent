# bash completion for the dami CLI (I4). Install: copy to /etc/bash_completion.d/dami
_dami_completions()
{
    local cur="${COMP_WORDS[COMP_CWORD]}"
    if [ "$COMP_CWORD" -eq 1 ]; then
        local verbs="today inbox recent read good bad meh trace health health-log health-reject domain domain-reject disclosures disclose-correct listen say stats recall ask context caption chat sessions session frontier brief approvals approve deny beliefs correct retract note board board-import"
        mapfile -t COMPREPLY < <(compgen -W "$verbs" -- "$cur")
        return
    fi
    case "${COMP_WORDS[1]}" in
        session)
            if [ "$COMP_CWORD" -eq 2 ]; then
                mapfile -t COMPREPLY < <(compgen -W "start resume interrupt turn reconnect" -- "$cur")
            fi
            ;;
        caption)
            mapfile -t COMPREPLY < <(compgen -f -- "$cur")
            ;;
    esac
}
complete -F _dami_completions dami
