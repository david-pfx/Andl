// Console based on jQuery-console for Andl REPL

var container = $('<div class="console">');
$('div.repl').append(container);
var controller = container.console({
    promptLabel: 'Andl> ',
    commandValidate: function (line) {
        if (line == "") return false;
        else return true;
    },
    commandHandle: function (line) {
        return [{
            msg: "=> " && line,
            className: "jquery-console-message-value"
        }]
        //return [{
        //    msg: "=> [12,42]",
        //    className: "jquery-console-message-value"
        //},
        //        {
        //            msg: ":: [a]",
        //            className: "jquery-console-message-type"
        //        }]
    },
    autofocus: true,
    animateScroll: true,
    promptHistory: true,
    charInsertTrigger: function (keycode, line) {
        return true;
        // Let you type until you press a-z
        // Never allow zero.
        //return !line.match(/[a-z]+/) && keycode != '0'.charCodeAt(0);
    }
});