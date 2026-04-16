<?php
declare(strict_types=1);
header('Content-Type: text/html');
?><!DOCTYPE html>
<html>
<body>
<script>
    try { window.parent.postMessage({type:'authResult'},'*'); } catch(_){}
    try { window.top.postMessage({type:'authResult'},'*'); } catch(_){}
</script>
</body>
</html>
