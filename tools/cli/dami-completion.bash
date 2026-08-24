# bash completion for the dami CLI (I4). Install: copy to /etc/bash_completion.d/dami
_dami_completions()
{
    local cur="${COMP_WORDS[COMP_CWORD]}"
    if [ "$COMP_CWORD" -eq 1 ]; then
        local verbs="inbox recent read good bad meh trace health stats recall ask context caption chat frontier brief approvals approve deny beliefs correct retract note"
        mapfile -t COMPREPLY < <(compgen -W "$verbs" -- "$cur")
        return
    fi
    case "${COMP_WORDS[1]}" in
        caption)
            mapfile -t COMPREPLY < <(compgen -f -- "$cur")
            ;;
    esac
}
complete -F _dami_completions dami
