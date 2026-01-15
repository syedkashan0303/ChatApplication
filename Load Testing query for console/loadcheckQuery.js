async function testing(roundNo = 1) {

    const MESSAGES_PER_GROUP = 20;
    const DELAY_BETWEEN_MESSAGES_MS = 200;
    const DELAY_BETWEEN_GROUPS_MS = 800;

    const sleep = (ms) => new Promise(res => setTimeout(res, ms));

    // ✅ idempotency client message id (same as your main code)
    function generateClientMessageId() {
        return Date.now().toString() + "-" + Math.random().toString(36).substring(2, 10);
    }

    const makeMsg = (groupName, i) =>
        `LoadTest | Round:${roundNo} | ${groupName} | Msg ${i + 1} | ${new Date().toLocaleString()}`;

    const groupItems = Array.from(document.querySelectorAll("#groupList .group-item"))
        .filter(x => String(x.dataset.isroom).toLowerCase() === "true");

    if (!groupItems.length) {
        console.log("❌ No groups found");
        return;
    }

    if (typeof connectionChat === "undefined") {
        console.log("❌ connectionChat not found");
        return;
    }

    // ✅ Ensure SignalR connected
    if (connectionChat.state !== "Connected") {
        try {
            await connectionChat.start();
            console.log("✅ SignalR started for testing()");
        } catch (err) {
            console.log("❌ SignalR start failed", err);
            return;
        }
    }

    for (let g = 0; g < groupItems.length; g++) {

        const item = groupItems[g];
        const roomName = item.dataset.room;

        // open room (UI click)
        item.click();
        await sleep(1000);

        const sender = $("#senderEmail").val() || "LoadTester";

        let sentCount = 0;
        let failCount = 0;

        for (let i = 0; i < MESSAGES_PER_GROUP; i++) {
            try {
                const clientMessageId = generateClientMessageId();
                const msg = makeMsg(roomName, i);

                // ✅ UPDATED: 4 params now
                await connectionChat.invoke("SendMessageToRoom", roomName, sender, msg, clientMessageId);

                sentCount++;
            } catch (err) {
                failCount++;
            }

            await sleep(DELAY_BETWEEN_MESSAGES_MS);
        }

        console.log(`✅ Group: ${roomName} | Sent: ${sentCount}/${MESSAGES_PER_GROUP} | Failed: ${failCount}`);

        await sleep(DELAY_BETWEEN_GROUPS_MS);
    }
}


// ✅ Run 10 rounds SEQUENTIALLY (avoid parallel spam)
(async function runLoadTest() {
    for (let r = 1; r <= 10; r++) {
        console.log(`🚀 Starting Round ${r}`);
        await testing(r);
        console.log(`🏁 Finished Round ${r}`);
    }
})();
