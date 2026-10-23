<?php
declare(strict_types=1);
header('Content-Type: text/html');
// Only a UUID-like nonce goes into the script, anything else is ignored
$nonce = isset($_GET['nonce']) && preg_match('/^[A-Za-z0-9-]{1,64}$/', (string) $_GET['nonce'])
    ? "'" . $_GET['nonce'] . "'"
    : 'undefined';
?><!DOCTYPE html>
<html>
<body>
<script>
    var msg = {type:'authResult',nonce:<?php echo $nonce; ?>};
    try { window.parent.postMessage(msg,'*'); } catch(_){}
    try { window.top.postMessage(msg,'*'); } catch(_){}
</script>
</body>
</html>
