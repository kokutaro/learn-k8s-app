const { Pool } = require("pg");
const express = require("express");
const cors = require("cors");

const app = express();
app.use(cors());
app.use(express.json());
const pool = new Pool({
  user: process.env.DB_USER,
  host: process.env.DB_HOST,
  database: process.env.DB_NAME,
  password: process.env.DB_PASSWORD,
  port: process.env.DB_PORT,
});

app.get("healthz", (req, res) => {
  res.status(200).json({ status: "ok" });
});

app.get("/api/v1/status", async (req, res) => {
  try {
    const client = await pool.connect();
    const result = await client.query("SELECT NOW()");
    res.json({ status: "ok", time: result.rows[0].now });
    client.release();
  } catch (err) {
    console.error(err);
    res
      .status(500)
      .json({ status: "error", message: "Database connection failed" });
  }
});

app.get("/api/v1/", async (req, res) => {
  res.json({ message: "Hello from the API!" });
});

const PORT = process.env.PORT || 3000;
app.listen(PORT, () => {
  console.log(`Server is running on port ${PORT}`);
});
