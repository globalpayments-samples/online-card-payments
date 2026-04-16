<?php
declare(strict_types=1);
header('Content-Type: text/html');
$nonce = isset($_GET['nonce']) ? json_encode((string) $_GET['nonce']) : 'undefined';
?><!DOCTYPE html>
<html>
<body>
<script>
    var msg = {type:'methodComplete',nonce:<?php echo $nonce; ?>};
    try { window.parent.postMessage(msg,'*'); } catch(_){}
    try { window.top.postMessage(msg,'*'); } catch(_){}
</script>
</body>
</html>
