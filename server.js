const express = require('express');
const bodyParser = require('body-parser');
const path = require('path');
const { validateUser, getProductData } = require('./database');
const WebSocket = require('ws');

const app = express();
const port = 3000;

app.use(bodyParser.json());
app.use(express.static(path.join(__dirname, 'public')));

app.post('/login', async (req, res) => {
    const { username, password } = req.body;

    try {
        const isValid = await validateUser(username, password);
        if (isValid) {
            console.log(`Login successful for user: ${username}`);
            res.status(200).json({ message: 'Login successful', redirectUrl: '/main-menu.html' });
        } else {
            console.log(`Invalid login attempt for user: ${username}`);
            res.status(401).json({ message: 'Invalid username or password' });
        }
    } catch (err) {
        console.error('Error during login attempt:', err);
        res.status(500).json({ message: 'Internal server error' });
    }
});

app.get('/api/get-data', async (req, res) => {
    try {
        const data = await getProductData();
        res.json(data);
    } catch (err) {
        console.error('Error fetching product data:', err);
        res.status(500).json({ message: 'Internal server error' });
    }
});

const server = app.listen(port, () => {
    console.log(`Server running on port ${port}`);
});

// WebSocket server setup
const wss = new WebSocket.Server({ server });

wss.on('connection', (ws) => {
    console.log('Client connected');

    ws.on('close', () => {
        console.log('Client disconnected');
    });
});

// Function to broadcast data to all connected clients
const broadcastData = async () => {
    try {
        const data = await getProductData();
        wss.clients.forEach((client) => {
            if (client.readyState === WebSocket.OPEN) {
                client.send(JSON.stringify(data));
            }
        });
    } catch (err) {
        console.error('Error broadcasting data:', err);
    }
};

// Example: Periodically broadcast data every 10 seconds
setInterval(broadcastData, 10000);