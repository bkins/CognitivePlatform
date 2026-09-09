window.backlogBoard = {
    downloadMarkdown: async (fileName, streamReference) => {
        const buffer = await streamReference.arrayBuffer();
        const url = URL.createObjectURL(new Blob([buffer], { type: "text/markdown;charset=utf-8" }));
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = fileName;
        anchor.click();
        URL.revokeObjectURL(url);
    },
    enableDragAutoScroll: () => {
        if (window.backlogBoard.dragAutoScrollEnabled) return;
        window.backlogBoard.dragAutoScrollEnabled = true;
        document.addEventListener("dragover", event => {
            const edge = 96;
            const maxStep = 28;
            if (event.clientY < edge)
                window.scrollBy(0, -Math.ceil(((edge - event.clientY) / edge) * maxStep));
            else if (event.clientY > window.innerHeight - edge)
                window.scrollBy(0, Math.ceil(((event.clientY - (window.innerHeight - edge)) / edge) * maxStep));
        });
    },
    scrollToElement: id => document.getElementById(id)?.scrollIntoView({ behavior: "smooth", block: "start" })
};
