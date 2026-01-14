

async function testing(roundNo = 1) {

    const MESSAGES_PER_GROUP = 20;
    const DELAY_BETWEEN_MESSAGES_MS = 200;
    const DELAY_BETWEEN_GROUPS_MS = 800;

    const makeMsg = (groupName, i) =>
        `LoadTest | Round:${roundNo} | ${groupName} | Msg ${i + 1} | ${new Date().toLocaleString()}`;

    const sleep = (ms) => new Promise(res => setTimeout(res, ms));

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

    if (connectionChat.state !== "Connected") {
        try {
            await connectionChat.start();
        } catch (err) {
            console.log("❌ SignalR start failed");
            return;
        }
    }

    for (let g = 0; g < groupItems.length; g++) {

        const item = groupItems[g];
        const roomName = item.dataset.room;

        // open room
        item.click();
        await sleep(1000);

        let sentCount = 0;

        for (let i = 0; i < MESSAGES_PER_GROUP; i++) {
            try {
                const sender = $("#senderEmail").val() || "LoadTester";
                await connectionChat.invoke("SendMessageToRoom", roomName, sender, makeMsg(roomName, i));
                sentCount++;
            } catch (err) {
                // skip failed
            }

            await sleep(DELAY_BETWEEN_MESSAGES_MS);
        }

        // ✅ ONLY THIS LOG
        console.log(`✅ Group: ${roomName} | Total Sent: ${sentCount}/${MESSAGES_PER_GROUP}`);

        await sleep(DELAY_BETWEEN_GROUPS_MS);
    }
}


for (let i = 0; i < 10; i++) {
    testing();
}